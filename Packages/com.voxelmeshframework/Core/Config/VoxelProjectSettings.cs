namespace Voxels.Core.Config
{
	using Unity.Logging;
	using UnityEngine;

	public sealed class VoxelProjectSettings : ScriptableObject
	{
		[Tooltip("Log level in Build.")]
		public LogLevel logLevelInGame = LogLevel.Warning;

		[Tooltip("Log level in Editor.")]
		public LogLevel logLevelInEditor = LogLevel.Info;

		[Header("Fences")]
		[Tooltip("Initial capacity for the per-entity JobHandle registry.")]
		[Min(1024)]
		public int fenceRegistryCapacity = 16384;
	}
}
