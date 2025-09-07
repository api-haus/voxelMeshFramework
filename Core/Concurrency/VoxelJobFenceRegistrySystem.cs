namespace Voxels.Core.Concurrency
{
	using Unity.Burst;
	using Unity.Entities;

	[RequireMatchingQueriesForUpdate]
	[UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
	public partial struct VoxelJobFenceRegistrySystem : ISystem
	{
		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			// Keep as a safe fallback; bootstrap system should initialize with configured capacity
			VoxelJobFenceRegistry.Initialize();
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state)
		{
			VoxelJobFenceRegistry.Release();
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state) { }
	}
}
