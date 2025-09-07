namespace Voxels.Core.Budgets
{
	using Unity.Burst;
	using Unity.Entities;
	using static Unity.Entities.SystemAPI;
	using EndInitST = Unity.Entities.EndInitializationEntityCommandBufferSystem.Singleton;

	[UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
	[RequireMatchingQueriesForUpdate]
	public partial struct VoxelBudgetsSystem : ISystem
	{
		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			state.RequireForUpdate<EndInitST>();
			state.EntityManager.CreateSingleton(new VoxelBudgets());
			VoxelBudgets.Current = VoxelBudgets.HeavyLoading;
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			var ecb = GetSingleton<EndInitST>().CreateCommandBuffer(state.WorldUnmanaged);

			ref var st = ref GetSingletonRW<VoxelBudgets>().ValueRW;

			foreach (
				var (changeRequest, entity) in
				//
				Query<RefRO<VoxelBudgetsChangeRequest>>() //
					.WithEntityAccess()
			)
			{
				st = changeRequest.ValueRO.newBudgets;
				VoxelBudgets.Current = st;

				ecb.DestroyEntity(entity);
			}
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state) { }
	}
}
