namespace Voxels.Core.Procedural.Generators
{
	using Spatial;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;

	public interface IProceduralVoxelGenerator
	{
		JobHandle Schedule(
			MinMaxAABB localBounds,
			float4x4 transform,
			float voxelSize,
			VoxelVolumeData data,
			JobHandle inputDeps
		);
	}
}
