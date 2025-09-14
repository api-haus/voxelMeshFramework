namespace Voxels.Core.Procedural
{
	using Spatial;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;

	public interface IProceduralVoxelGenerator
	{
		JobHandle ScheduleVoxels(
			MinMaxAABB localBounds,
			float4x4 transform,
			float voxelSize,
			VoxelVolumeData data,
			JobHandle inputDeps
		);
	}
}
