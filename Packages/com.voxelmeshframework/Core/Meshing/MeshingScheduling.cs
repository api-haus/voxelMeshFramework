namespace Voxels.Core.Meshing
{
	using System;
	using Unity.Jobs;

	public static class MeshingScheduling
	{
		public static JobHandle ScheduleAlgorithm(
			in MeshingInputData input,
			in MeshingOutputData output,
			in VoxelMeshingAlgorithmComponent algorithm,
			ref FairingBuffers fairing,
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
				VoxelMeshingAlgorithm.FAIRED_SURFACE_NETS => new NaiveSurfaceNetsFairingScheduler
				{
					cellMargin = algorithm.cellMargin,
					fairingBuffers = fairing,
					fairingStepSize = algorithm.fairingStepSize,
					fairingIterations = algorithm.fairingIterations,
					recomputeNormalsAfterFairing = algorithm.recomputeNormalsAfterFairing,
					seamConstraintMode = algorithm.seamConstraintMode,
					seamConstraintWeight = algorithm.seamConstraintWeight,
					seamBandWidth = algorithm.seamBandWidth,
				}.Schedule(input, output, deps),
				_ => throw new NotImplementedException($"Algorithm {algorithm.algorithm} not implemented"),
			};
		}
	}
}
