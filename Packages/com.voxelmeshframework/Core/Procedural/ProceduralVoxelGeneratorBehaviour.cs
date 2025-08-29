namespace Voxels.Core.Procedural
{
	using Spatial;
	using Unity.Jobs;
	using Unity.Mathematics.Geometry;
	using UnityEngine;

	public abstract class ProceduralVoxelGeneratorBehaviour : MonoBehaviour, IProceduralVoxelGenerator
	{
		public abstract JobHandle Schedule(
			MinMaxAABB bounds,
			float voxelSize,
			VoxelVolumeData data,
			JobHandle inputDeps
		);
	}
}
