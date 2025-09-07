// Ensure logging level defaults to Info in both Editor and Player

namespace Voxels.Core
{
	using System.IO;
	using Config;
	using Unity.Logging;
	using Unity.Logging.Sinks;
	using Application = UnityEngine.Application;

	public static class VoxelLogger
	{
		public static void InitializeFromConfiguration()
		{
			var cfg = new LoggerConfig();

			cfg = cfg.MinimumLevel.Set(
				Application.isEditor
					? VoxelProjectConfiguration.Get().logLevelInEditor
					: VoxelProjectConfiguration.Get().logLevelInGame
			);

#if UNITY_EDITOR
			cfg = cfg.WriteTo.UnityEditorConsole(
				outputTemplate: "{Level} | {Message}{NewLine}{Stacktrace}"
			);
#endif

			cfg = cfg.WriteTo.JsonFile(Path.Join(Application.persistentDataPath, "latest.log"));

			var lm = LogMemoryManagerParameters.Default;

			lm.InitialBufferCapacity = 1024 * 1024 * 2; // 2mb

			Log.Logger = cfg.CreateLogger(lm);
		}
	}
}
