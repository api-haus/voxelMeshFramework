namespace Voxels.Core.Meshing.Systems
{
	using Concurrency;
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

			// apply managed mesh
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
