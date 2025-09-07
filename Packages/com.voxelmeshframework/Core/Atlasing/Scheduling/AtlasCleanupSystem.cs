namespace Voxels.Core.Atlasing.Scheduling
{
	using Components;
	using Unity.Burst;
	using Unity.Entities;
	using static Unity.Entities.SystemAPI;
	using EndInitST = Unity.Entities.EndInitializationEntityCommandBufferSystem.Singleton;

	[RequireMatchingQueriesForUpdate]
	[UpdateInGroup(typeof(InitializationSystemGroup))]
	public partial struct AtlasCleanupSystem : ISystem
	{
		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			state.RequireForUpdate<EndInitST>();
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			var ecb = GetSingleton<EndInitST>().CreateCommandBuffer(state.WorldUnmanaged);

			foreach (
				var (nativeChunkAtlasRef, entity) in Query<RefRW<NativeChunkAtlas>>()
					.WithNone<NativeChunkAtlas.CleanupTag>()
					.WithEntityAccess()
			)
			{
				nativeChunkAtlasRef.ValueRW.Dispose();

				ecb.RemoveComponent<NativeChunkAtlas>(entity);
			}
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state)
		{
			foreach (var nativeChunkAtlasRef in Query<RefRW<NativeChunkAtlas>>())
				nativeChunkAtlasRef.ValueRW.Dispose();
		}
	}
}
