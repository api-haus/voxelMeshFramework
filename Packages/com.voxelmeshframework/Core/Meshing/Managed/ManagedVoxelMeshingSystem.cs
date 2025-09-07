namespace Voxels.Core.Meshing.Managed
{
	using Atlasing.Components;
	using Budgets;
	using Components;
	using Concurrency;
	using Hybrid;
	using Hybrid.GameObjectCollision;
	using Hybrid.GameObjectRendering;
	using Tags;
	using Unity.Entities;
	using Unity.Logging;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Entities.SystemAPI;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;
	using EntityCommandBuffer = Unity.Entities.EntityCommandBuffer;
	using SystemAPI = Unity.Entities.SystemAPI;
	using SystemBase = Unity.Entities.SystemBase;
	using VoxelMeshingSystem = Scheduling.VoxelMeshingSystem;

	/// <summary>
	///   Applies managed Mesh updates for voxel chunks and coordinates atomic presentation commits for rolling grids.
	///   Skips per-entity mesh application while a grid is batching or committing to guarantee no partial visual updates.
	///   On commit, swaps staged meshes to front and moves authoring GameObjects to their new origins in one frame.
	///   Also refreshes MeshFilter and MeshCollider attachments once job fences complete.
	/// </summary>
	[RequireMatchingQueriesForUpdate]
	[UpdateAfter(typeof(VoxelMeshingSystem))]
	public partial class ManagedVoxelMeshingSystem : SystemBase
	{
		/// <summary>
		///   Orchestrates the frame: applies managed meshes for entities whose fences have completed.
		/// </summary>
		protected override void OnUpdate()
		{
			using var _ = ManagedVoxelMeshingSystem_Update.Auto();

			var ecb = SystemAPI.GetSingleton<EndSimST>().CreateCommandBuffer(World.Unmanaged);

			ApplyManagedMeshesForReadyEntities(ref ecb);
		}

		/// <summary>
		///   Applies managed mesh data to entities marked with <see cref="NeedsManagedMeshUpdate" /> whose job fences have
		///   completed.
		///   Skips chunks that belong to grids currently batching or committing to preserve atomic presentation for rolling grids.
		///   On first successful mesh per chunk, updates per-grid meshing progress.
		/// </summary>
		/// <param name="ecb">Command buffer for structural changes.</param>
		void ApplyManagedMeshesForReadyEntities(ref EntityCommandBuffer ecb)
		{
			var toProcess = VoxelBudgets.Current.perFrame.meshApplied;

			foreach (
				var (nativeVoxelMeshRef, entity) in Query<RefRW<NativeVoxelMesh>>()
					.WithAll<NeedsManagedMeshUpdate>()
					.WithEntityAccess()
			)
			{
				if (!VoxelJobFenceRegistry.TryComplete(entity))
					continue;

				using var _ = ManagedVoxelMeshingSystem_ApplyMesh.Auto();

				ref var nvm = ref nativeVoxelMeshRef.ValueRW;
				if (nvm.meshing.meshData.Length == 0)
				{
					Log.Warning("applying and mesh data is zero");
					continue;
				}

				nvm.ApplyMeshManaged();

				// Present immediately after apply
				var indexCount = nvm.meshing.indices.Length;
				var hasMesh = indexCount > 16;

				if (EntityManager.HasComponent<EntityMeshFilterAttachment>(entity))
				{
					var meshFilterAttachment = EntityManager.GetComponentObject<EntityMeshFilterAttachment>(
						entity
					);
					if (meshFilterAttachment != null && meshFilterAttachment.attachTo != null)
					{
						meshFilterAttachment.attachTo.sharedMesh = hasMesh ? nvm.meshing.meshRef : null;
						meshFilterAttachment.attachTo.gameObject.SetActive(hasMesh);
					}
				}

				if (EntityManager.HasComponent<EntityMeshColliderAttachment>(entity))
				{
					var meshColliderAttachment =
						EntityManager.GetComponentObject<EntityMeshColliderAttachment>(entity);
					if (meshColliderAttachment != null && meshColliderAttachment.attachTo != null)
					{
						meshColliderAttachment.attachTo.sharedMesh = hasMesh ? nvm.meshing.meshRef : null;
						meshColliderAttachment.attachTo.enabled = hasMesh;
					}
				}

				ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(entity, false);

				if (EntityManager.HasComponent<AtlasedChunk>(entity))
				{
					var atlasId = EntityManager.GetComponentData<AtlasedChunk>(entity).atlasId;

					if (VoxelEntityBridge.TryGetAtlas(atlasId, out var atlas))
					{
						var pendingCounter = atlas.counters.Pending().Sub(1) - 1;

						Log.Verbose("pending-- {0}", pendingCounter);
					}
				}

				if (--toProcess <= 0)
					return;
			}
		}
	}
}
