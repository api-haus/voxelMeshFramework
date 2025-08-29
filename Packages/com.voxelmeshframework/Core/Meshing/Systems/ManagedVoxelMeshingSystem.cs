namespace Voxels.Core.Meshing.Systems
{
	using Hybrid;
	using Tags;
	using Unity.Burst;
	using Unity.Entities;
	using static Unity.Entities.SystemAPI;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;

	[RequireMatchingQueriesForUpdate]
	[UpdateAfter(typeof(VoxelMeshingSystem))]
	public partial class ManagedVoxelMeshingSystem : SystemBase
	{
		[BurstDiscard]
		protected override void OnUpdate()
		{
			var ecb = SystemAPI.GetSingleton<EndSimST>().CreateCommandBuffer(World.Unmanaged);

			// apply managed mesh
			foreach (
				var (nativeVoxelMeshRef, entity) in Query<RefRW<NativeVoxelMesh>>()
					.WithAll<NeedsManagedMeshUpdate>()
					.WithEntityAccess()
			)
			{
				ref var nvm = ref nativeVoxelMeshRef.ValueRW;

				nvm.ApplyMeshManaged();

				ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(entity, false);
			}

			// attach w/ mesh filter
			foreach (
				var (nativeVoxelMeshRef, meshFilterAttachment) in Query<
					RefRW<NativeVoxelMesh>,
					EntityMeshFilterAttachment
				>()
					.WithAll<NeedsManagedMeshUpdate>()
			)
			{
				ref var nvm = ref nativeVoxelMeshRef.ValueRW;

				meshFilterAttachment.attachTo.sharedMesh = nvm.meshing.meshRef;
			}

			// attach w/ mesh collider
			foreach (
				var (nativeVoxelMeshRef, meshColliderAttachment) in Query<
					RefRW<NativeVoxelMesh>,
					EntityMeshColliderAttachment
				>()
					.WithAll<NeedsManagedMeshUpdate>()
			)
			{
				ref var nvm = ref nativeVoxelMeshRef.ValueRW;

				var indexCount = nvm.meshing.indices.Length;
				var hasMesh = indexCount > 16;

				meshColliderAttachment.attachTo.sharedMesh = hasMesh ? nvm.meshing.meshRef : null;
				meshColliderAttachment.attachTo.enabled = hasMesh;
			}
		}
	}
}
