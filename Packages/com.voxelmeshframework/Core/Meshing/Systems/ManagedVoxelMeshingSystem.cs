namespace Voxels.Core.Meshing.Systems
{
	using Concurrency;
	using Grids;
	using Hybrid;
	using Tags;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using UnityEngine;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Entities.SystemAPI;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;

	[RequireMatchingQueriesForUpdate]
	[UpdateAfter(typeof(VoxelMeshingSystem))]
	public partial class ManagedVoxelMeshingSystem : SystemBase
	{
		[BurstDiscard]
		protected override void OnUpdate()
		{
			using var _ = ManagedVoxelMeshingSystem_Update.Auto();

			var ecb = SystemAPI.GetSingleton<EndSimST>().CreateCommandBuffer(World.Unmanaged);

			// Gather any committing or batching grids first to gate per-entity path
			var committingQuery = QueryBuilder()
				.WithAll<RollingGridCommitEvent, NativeVoxelGrid>()
				.Build();
			using var committingGrids = committingQuery.ToComponentDataArray<NativeVoxelGrid>(
				Allocator.Temp
			);
			var batchingQuery = QueryBuilder().WithAll<RollingGridBatchActive, NativeVoxelGrid>().Build();
			using var batchingGrids = batchingQuery.ToComponentDataArray<NativeVoxelGrid>(Allocator.Temp);

			// apply managed mesh (per-entity readiness) only for entities not part of a committing grid
			foreach (
				var (nativeVoxelMeshRef, entity) in Query<RefRW<NativeVoxelMesh>>()
					.WithAll<NeedsManagedMeshUpdate>()
					.WithEntityAccess()
			)
				using (ManagedVoxelMeshingSystem_ApplyMesh.Auto())
				{
					// If this entity is a chunk and its grid is committing or batch-active, skip here (atomic swap path will handle)
					if (
						EntityManager.HasComponent<NativeVoxelChunk>(entity)
						&& (committingGrids.Length > 0 || batchingGrids.Length > 0)
					)
					{
						var chunk = EntityManager.GetComponentData<NativeVoxelChunk>(entity);
						var skip = false;
						for (var i = 0; i < committingGrids.Length; i++)
							if (committingGrids[i].gridID == chunk.gridID)
							{
								skip = true;
								break;
							}

						if (!skip)
							for (var i = 0; i < batchingGrids.Length; i++)
								if (batchingGrids[i].gridID == chunk.gridID)
								{
									skip = true;
									break;
								}

						if (skip)
							continue;
					}

					// Only proceed when the per-entity fence is complete
					if (!VoxelJobFenceRegistry.TryComplete(entity))
						continue;
					ref var nvm = ref nativeVoxelMeshRef.ValueRW;
					// Skip if mesh data has not been allocated by the meshing/upload pipeline
					if (nvm.meshing.meshData.Length == 0)
						continue;
					nvm.ApplyMeshManaged();
					ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(entity, false);

					// Progress: if this entity is a chunk and hasn't been counted yet, enable MeshedOnce and increment grid progress
					if (EntityManager.HasComponent<NativeVoxelChunk>(entity))
					{
						var gid = EntityManager.GetComponentData<NativeVoxelChunk>(entity).gridID;
						var firstTime = true;
						if (EntityManager.HasComponent<MeshedOnce>(entity))
							firstTime = !EntityManager.IsComponentEnabled<MeshedOnce>(entity);
						else
							ecb.AddComponent<MeshedOnce>(entity);
						ecb.SetComponentEnabled<MeshedOnce>(entity, true);

						if (firstTime)
						{
							// find matching grid entity by gridID
							var gq = QueryBuilder().WithAll<NativeVoxelGrid, GridMeshingProgress>().Build();
							using var grids = gq.ToEntityArray(Allocator.Temp);
							using var gridDatas = gq.ToComponentDataArray<NativeVoxelGrid>(Allocator.Temp);
							for (var i = 0; i < grids.Length; i++)
							{
								if (gridDatas[i].gridID != gid)
									continue;
								var grid = grids[i];
								var prog = EntityManager.GetComponentData<GridMeshingProgress>(grid);
								prog.meshedOnceCount++;
								ecb.SetComponent(grid, prog);
								if (
									!prog.firedOnce
									&& prog.totalChunks > 0
									&& prog.meshedOnceCount >= prog.totalChunks
								)
								{
									prog.firedOnce = true;
									ecb.SetComponent(grid, prog);
									if (!EntityManager.HasComponent<NativeVoxelGrid.FullyMeshedEvent>(grid))
										ecb.AddComponent<NativeVoxelGrid.FullyMeshedEvent>(grid);
									ecb.SetComponentEnabled<NativeVoxelGrid.FullyMeshedEvent>(grid, true);
								}
								break;
							}
						}
					}
				}

			// atomic grid commit: gather grids that raised RollingGridCommitEvent
			var commitQuery = QueryBuilder().WithAll<RollingGridCommitEvent, NativeVoxelGrid>().Build();
			using var commitGrids = commitQuery.ToComponentDataArray<NativeVoxelGrid>(Allocator.Temp);
			using var commitEvents = commitQuery.ToComponentDataArray<RollingGridCommitEvent>(
				Allocator.Temp
			);
			if (commitGrids.Length > 0)
			{
				// apply for all chunks whose NativeVoxelChunk.gridID matches any committing gridID
				foreach (
					var (nativeVoxelMeshRef, chunk, entity) in Query<
						RefRW<NativeVoxelMesh>,
						RefRO<NativeVoxelChunk>
					>()
						.WithAll<NeedsManagedMeshUpdate>()
						.WithEntityAccess()
				)
					using (ManagedVoxelMeshingSystem_ApplyMesh.Auto())
					{
						var gid = chunk.ValueRO.gridID;
						var match = false;
						for (var i = 0; i < commitGrids.Length; i++)
							if (commitGrids[i].gridID == gid)
							{
								match = true;
								break;
							}

						if (!match)
							continue;

						if (!VoxelJobFenceRegistry.TryComplete(entity))
							continue;
						ref var nvm = ref nativeVoxelMeshRef.ValueRW;
						if (nvm.meshing.meshData.Length == 0)
							continue;
						nvm.ApplyMeshManaged();
						ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(entity, false);
					}

				// move grid anchor and chunk GameObjects to new origins (authoritative GO transforms)
				using (var eventEntities = commitQuery.ToEntityArray(Allocator.Temp))
				{
					for (var gi = 0; gi < eventEntities.Length; gi++)
					{
						var gridEntity = eventEntities[gi];
						var evt = commitEvents[gi];
						var gridData = commitGrids[gi];
						var stride = gridData.voxelSize * VoxelConstants.EFFECTIVE_CHUNK_SIZE;

						// Move grid anchor
						if (EntityManager.HasComponent<EntityGameObjectTransformAttachment>(gridEntity))
						{
							var at = EntityManager.GetComponentObject<EntityGameObjectTransformAttachment>(
								gridEntity
							);
							if (at != null && at.attachTo)
								at.attachTo.position = evt.targetOriginWorld;
						}

						// Move chunk GOs under this grid to slot origins relative to anchor
						if (EntityManager.HasBuffer<LinkedEntityGroup>(gridEntity))
						{
							var leg = EntityManager.GetBuffer<LinkedEntityGroup>(gridEntity);
							for (var i = 1; i < leg.Length; i++)
							{
								var chunk = leg[i].Value;
								if (!EntityManager.HasComponent<NativeVoxelChunk>(chunk))
									continue;
								if (!EntityManager.HasComponent<EntityGameObjectTransformAttachment>(chunk))
									continue;
								var slot = EntityManager.GetComponentData<NativeVoxelChunk>(chunk).coord;
								var atChunk = EntityManager.GetComponentObject<EntityGameObjectTransformAttachment>(
									chunk
								);
								if (atChunk != null && atChunk.attachTo)
									atChunk.attachTo.localPosition = new Vector3(
										slot.x * stride,
										slot.y * stride,
										slot.z * stride
									);
							}
						}

						// clear batch active flag if present
						if (EntityManager.HasComponent<RollingGridBatchActive>(gridEntity))
							ecb.SetComponentEnabled<RollingGridBatchActive>(gridEntity, false);

						// disable commit event after processing
						ecb.SetComponentEnabled<RollingGridCommitEvent>(gridEntity, false);
					}
				}
			}

			// attach w/ mesh filter
			foreach (
				var (nativeVoxelMeshRef, entity) in Query<RefRW<NativeVoxelMesh>>()
					.WithAll<NeedsManagedMeshUpdate>()
					.WithEntityAccess()
			)
				using (ManagedVoxelMeshingSystem_AttachMeshFilter.Auto())
				{
					if (!VoxelJobFenceRegistry.TryComplete(entity))
						continue;

					if (!EntityManager.HasComponent<EntityMeshFilterAttachment>(entity))
						continue;
					var meshFilterAttachment = EntityManager.GetComponentObject<EntityMeshFilterAttachment>(
						entity
					);
					if (meshFilterAttachment == null || meshFilterAttachment.attachTo == null)
						continue;

					ref var nvm = ref nativeVoxelMeshRef.ValueRW;
					var indexCount = nvm.meshing.indices.Length;
					var hasMesh = indexCount > 16;
					meshFilterAttachment.attachTo.sharedMesh = hasMesh ? nvm.meshing.meshRef : null;
				}

			// attach w/ mesh collider
			foreach (
				var (nativeVoxelMeshRef, entity) in Query<RefRW<NativeVoxelMesh>>()
					.WithAll<NeedsManagedMeshUpdate>()
					.WithEntityAccess()
			)
				using (ManagedVoxelMeshingSystem_AttachMeshCollider.Auto())
				{
					if (!VoxelJobFenceRegistry.TryComplete(entity))
						continue;

					if (!EntityManager.HasComponent<EntityMeshColliderAttachment>(entity))
						continue;
					var meshColliderAttachment =
						EntityManager.GetComponentObject<EntityMeshColliderAttachment>(entity);
					if (meshColliderAttachment == null || meshColliderAttachment.attachTo == null)
						continue;

					ref var nvm = ref nativeVoxelMeshRef.ValueRW;
					var indexCount = nvm.meshing.indices.Length;
					var hasMesh = indexCount > 16;
					meshColliderAttachment.attachTo.sharedMesh = hasMesh ? nvm.meshing.meshRef : null;
					meshColliderAttachment.attachTo.enabled = hasMesh;
				}
		}
	}
}
