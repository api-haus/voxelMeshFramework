namespace Voxels.Core.Meshing.Systems
{
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using static Unity.Entities.SystemAPI;
	using EndInitST = Unity.Entities.EndInitializationEntityCommandBufferSystem.Singleton;

	[RequireMatchingQueriesForUpdate]
	[UpdateInGroup(typeof(InitializationSystemGroup))]
	public partial struct VoxelMeshAllocationSystem : ISystem
	{
		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			state.RequireForUpdate<EndInitST>();

			foreach (var nativeVoxelMesh in Query<RefRW<NativeVoxelMesh>>())
				nativeVoxelMesh.ValueRW.Dispose();
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state)
		{
			foreach (var nativeVoxelMesh in Query<RefRW<NativeVoxelMesh>>())
				nativeVoxelMesh.ValueRW.Dispose();
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			var ecb = GetSingleton<EndInitST>().CreateCommandBuffer(state.WorldUnmanaged);

			// allocate
			foreach (
				var (req, entity) in Query<RefRO<NativeVoxelMesh.Request>>()
					.WithNone<NativeVoxelMesh>()
					.WithEntityAccess()
			)
			{
				var nvm = new NativeVoxelMesh(Allocator.Persistent);

				nvm.volume.voxelSize = req.ValueRO.voxelSize;

				ecb.AddComponent(entity, nvm);
			}

			// cleanup
			foreach (
				var (nativeVoxelMesh, entity) in Query<RefRW<NativeVoxelMesh>>()
					.WithNone<NativeVoxelMesh.Request>()
					.WithEntityAccess()
			)
			{
				// dispose
				state.Dependency = nativeVoxelMesh.ValueRW.Dispose(state.Dependency);

				ecb.RemoveComponent<NativeVoxelMesh>(entity);
			}
		}
	}
}
