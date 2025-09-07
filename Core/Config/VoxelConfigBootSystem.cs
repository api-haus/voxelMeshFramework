namespace Voxels.Core.Config
{
	using Unity.Burst;
	using Unity.Entities;

	[RequireMatchingQueriesForUpdate]
	public partial struct VoxelConfigBootSystem : ISystem
	{
		[BurstDiscard]
		public void OnCreate(ref SystemState state)
		{
			VoxelProjectConfiguration.RuntimeInit();
			VoxelLogger.InitializeFromConfiguration();
		}

		[BurstDiscard]
		public void OnUpdate(ref SystemState state) { }

		[BurstDiscard]
		public void OnDestroy(ref SystemState state) { }
	}
}
