namespace Voxels.Core.Config
{
	using Concurrency;
	using Unity.Entities;
	using UnityEngine;

	[WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
	[UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
	public partial struct VoxelProjectBootstrapSystem : ISystem
	{
		public void OnCreate(ref SystemState state)
		{
			VoxelProjectConfiguration.InitializeDefaults();
			// Attempt to find a project settings asset in Resources (optional pattern)
			var settings = Resources.Load<VoxelProjectSettings>("VoxelProjectSettings");
			VoxelProjectConfiguration.ApplyFromSettings(settings);

			// Initialize fence registry with configured capacity
			var cfg = VoxelProjectConfiguration.Get();
			VoxelJobFenceRegistry.Initialize(cfg.fenceRegistryCapacity);
		}

		public void OnDestroy(ref SystemState state) { }

		public void OnUpdate(ref SystemState state) { }
	}
}
