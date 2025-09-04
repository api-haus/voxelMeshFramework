namespace Voxels.Core.Procedural
{
	using Spatial;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using UnityEngine;

	public abstract class ProceduralVoxelGeneratorBehaviour : MonoBehaviour, IProceduralVoxelGenerator
	{
		public abstract JobHandle Schedule(
			MinMaxAABB localBounds,
			float4x4 ltw,
			float voxelSize,
			VoxelVolumeData data,
			JobHandle inputDeps
		);
	}
}
