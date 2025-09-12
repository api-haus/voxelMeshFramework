namespace Voxels.Core.Meshing.Scheduling
{
	using System;
	using Algorithms;
	using Algorithms.SurfaceNets;
	using Unity.Jobs;

	public static class MeshingScheduling
	{
		public static JobHandle ScheduleAlgorithm(
			in MeshingInputData input,
			in MeshingOutputData output,
			in VoxelMeshingAlgorithmComponent algorithm,
			JobHandle deps = default
		)
		{
			return algorithm.algorithm switch
			{
				VoxelMeshingAlgorithm.NAIVE_SURFACE_NETS => new NaiveSurfaceNetsScheduler().Schedule(
					input,
					output,
					deps
				),
				_ => throw new NotImplementedException($"Algorithm {algorithm.algorithm} not implemented"),
			};
		}
	}
}
