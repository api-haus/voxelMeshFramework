namespace Voxels.Core.Meshing.Systems
{
	using Hybrid;
	using Tags;
	using Unity.Burst;
	using Unity.Entities;
	using Voxels.Core.Concurrency;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Entities.SystemAPI;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;

	[WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
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
					nvm.ApplyMeshManaged();
					ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(entity, false);
				}

			// attach w/ mesh filter
			foreach (
				var (nativeVoxelMeshRef, meshFilterAttachment, entity) in Query<
					RefRW<NativeVoxelMesh>,
					EntityMeshFilterAttachment
				>()
					.WithAll<NeedsManagedMeshUpdate>()
					.WithEntityAccess()
			)
				using (ManagedVoxelMeshingSystem_AttachMeshFilter.Auto())
				{
					if (!VoxelJobFenceRegistry.TryComplete(entity))
						continue;
					ref var nvm = ref nativeVoxelMeshRef.ValueRW;
					var indexCount = nvm.meshing.indices.Length;
					var hasMesh = indexCount > 16;
					meshFilterAttachment.attachTo.sharedMesh = hasMesh ? nvm.meshing.meshRef : null;
				}

			// attach w/ mesh collider
			foreach (
				var (nativeVoxelMeshRef, meshColliderAttachment, entity) in Query<
					RefRW<NativeVoxelMesh>,
					EntityMeshColliderAttachment
				>()
					.WithAll<NeedsManagedMeshUpdate>()
					.WithEntityAccess()
			)
				using (ManagedVoxelMeshingSystem_AttachMeshCollider.Auto())
				{
					if (!VoxelJobFenceRegistry.TryComplete(entity))
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
