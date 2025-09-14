namespace Voxels.Core.Procedural
{
	using Spatial;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;

	public interface IProceduralMaterialGenerator
	{
		JobHandle ScheduleMaterials(
			MinMaxAABB localBounds,
			float4x4 transform,
			float voxelSize,
			VoxelVolumeData data,
			JobHandle inputDeps
		);
	}
}
