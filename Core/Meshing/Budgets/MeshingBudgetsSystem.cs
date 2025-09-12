namespace Voxels.Core.Meshing.Budgets
{
	using Unity.Burst;
	using Unity.Entities;
	using Unity.Logging;
	using static Unity.Entities.SystemAPI;
	using EndInitST = Unity.Entities.EndInitializationEntityCommandBufferSystem.Singleton;

	[UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
	[RequireMatchingQueriesForUpdate]
	public partial struct MeshingBudgetsSystem : ISystem
	{
		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			state.RequireForUpdate<EndInitST>();
			state.EntityManager.CreateSingleton(new MeshingBudgets());
			MeshingBudgets.Current = MeshingBudgets.HeavyLoading;
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			var ecb = GetSingleton<EndInitST>().CreateCommandBuffer(state.WorldUnmanaged);

			ref var st = ref GetSingletonRW<MeshingBudgets>().ValueRW;

			foreach (
				var (changeRequest, entity) in
				//
				Query<RefRO<MeshingBudgetsChangeRequest>>() //
					.WithEntityAccess()
			)
			{
				st = changeRequest.ValueRO.newBudgets;
				MeshingBudgets.Current = st;

				Log.Debug("new budget {0}", st);

				ecb.DestroyEntity(entity);
			}
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state) { }
	}
}
