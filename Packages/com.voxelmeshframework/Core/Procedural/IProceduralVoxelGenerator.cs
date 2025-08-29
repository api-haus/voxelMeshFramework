namespace Voxels.Core.Procedural
{
	using Spatial;
	using Unity.Jobs;
	using Unity.Mathematics.Geometry;

	public interface IProceduralVoxelGenerator
	{
		JobHandle Schedule(
			MinMaxAABB bounds,
			float voxelSize,
			VoxelVolumeData data,
			JobHandle inputDeps
		);
	}
}
