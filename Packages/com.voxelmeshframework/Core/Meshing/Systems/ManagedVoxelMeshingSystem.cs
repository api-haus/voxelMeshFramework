namespace Voxels.Core.Meshing.Systems
{
	using Concurrency;
	using Grids;
	using Hybrid;
	using Tags;
	using Unity.Burst;
	using Unity.Entities;
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

			// apply managed mesh (per-entity readiness)
			foreach (
				var (nativeVoxelMeshRef, entity) in Query<RefRW<NativeVoxelMesh>>()
					.WithAll<NeedsManagedMeshUpdate>()
					.WithEntityAccess()
			)
				using (ManagedVoxelMeshingSystem_ApplyMesh.Auto())
				{
					// Only proceed when the per-entity fence is complete
					if (!VoxelJobFenceRegistry.TryComplete(entity))
						continue;
					ref var nvm = ref nativeVoxelMeshRef.ValueRW;
					// Skip if mesh data has not been allocated by the meshing/upload pipeline
					if (nvm.meshing.meshData.Length == 0)
						continue;
					nvm.ApplyMeshManaged();
					ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(entity, false);
				}

			// atomic grid commit: gather grids that raised RollingGridCommitEvent
			var commitQuery = SystemAPI
				.QueryBuilder()
				.WithAll<RollingGridCommitEvent, NativeVoxelGrid>()
				.Build();
			using var commitGrids = commitQuery.ToComponentDataArray<NativeVoxelGrid>(Allocator.Temp);
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

				// disable commit events after processing
				using var eventEntities = commitQuery.ToEntityArray(Allocator.Temp);
				for (var i = 0; i < eventEntities.Length; i++)
					ecb.SetComponentEnabled<RollingGridCommitEvent>(eventEntities[i], false);
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
