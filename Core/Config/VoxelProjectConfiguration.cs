namespace Voxels.Core.Config
{
	using Concurrency;
	using Unity.Burst;
	using Unity.Logging;
	using UnityEngine;

	public struct VoxelProjectConfig
	{
		public bool loaded;
		public LogLevel logLevelInGame;
		public LogLevel logLevelInEditor;
		public int fenceRegistryCapacity;
	}

	public static class VoxelProjectConfiguration
	{
		static readonly SharedStatic<VoxelProjectConfig> s_config =
			SharedStatic<VoxelProjectConfig>.GetOrCreate<VoxelProjectConfig>();

		internal static void RuntimeInit()
		{
			InitializeDefaults();

			// Attempt to find a project settings asset in Resources (optional pattern)
			var settings = Resources.Load<VoxelProjectSettings>("VoxelProjectSettings");
			ApplyFromSettings(settings);

			// Initialize fence registry with configured capacity
			var cfg = Get();
			VoxelJobFenceRegistry.Initialize(cfg.fenceRegistryCapacity);
		}

		public static void InitializeDefaults()
		{
			// Only set defaults once: detect uninitialized state by fenceRegistryCapacity == 0
			if (s_config.Data.fenceRegistryCapacity == 0)
				s_config.Data = new VoxelProjectConfig
				{
					fenceRegistryCapacity = 16384,
					logLevelInEditor = LogLevel.Debug,
					logLevelInGame = LogLevel.Warning,
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
				loaded = true,
				logLevelInGame = settings.logLevelInGame,
				logLevelInEditor = settings.logLevelInEditor,
				fenceRegistryCapacity = settings.fenceRegistryCapacity,
			};
		}
	}
}
