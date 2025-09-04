namespace Voxels.Core.Config
{
	using UnityEngine;

	public sealed class VoxelProjectSettings : ScriptableObject
	{
		[Header("Scheduling")]
		public MeshSchedulingPolicy meshSchedulingPolicy = MeshSchedulingPolicy.WAIT_AND_DEBOUNCE;

		[Header("Fences")]
		[Tooltip("Initial capacity for the per-entity JobHandle registry.")]
		[Min(1024)]
		public int fenceRegistryCapacity = 16384;
	}
}
