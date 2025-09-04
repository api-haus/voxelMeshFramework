namespace Voxels.Core.Config
{
	using Unity.Burst;
	using UnityEngine;

	public enum MeshSchedulingPolicy : byte
	{
		[InspectorName("Debounce updates")]
		WAIT_AND_DEBOUNCE = 0,

		[InspectorName("Fire&Forget style schedule")]
		TAIL_AND_PIPELINE = 1,
	}

	public struct VoxelProjectConfig
	{
		public MeshSchedulingPolicy meshSchedulingPolicy;
		public int fenceRegistryCapacity;
	}

	public static class VoxelProjectConfiguration
	{
		static readonly SharedStatic<VoxelProjectConfig> s_config =
			SharedStatic<VoxelProjectConfig>.GetOrCreate<VoxelProjectConfig>();

		public static bool IsCreated => true; // SharedStatic<T> is always available once type is initialized

		public static void InitializeDefaults()
		{
			// Only set defaults once: detect uninitialized state by fenceRegistryCapacity == 0
			if (s_config.Data.fenceRegistryCapacity == 0)
				s_config.Data = new VoxelProjectConfig
				{
					meshSchedulingPolicy = MeshSchedulingPolicy.WAIT_AND_DEBOUNCE,
					fenceRegistryCapacity = 16384,
				};
		}

		public static VoxelProjectConfig Get()
		{
			return s_config.Data;
		}

		public static void Set(VoxelProjectConfig config)
		{
			s_config.Data = config;
		}

		public static void ApplyFromSettings(VoxelProjectSettings settings)
		{
			if (settings == null)
			{
				InitializeDefaults();
				return;
			}

			s_config.Data = new VoxelProjectConfig
			{
				meshSchedulingPolicy = settings.meshSchedulingPolicy,
				fenceRegistryCapacity = settings.fenceRegistryCapacity,
			};
		}
	}
}
