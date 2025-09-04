namespace Voxels.Core.Meshing
{
	using ThirdParty.SurfaceNets;
	using Unity.Burst;
	using Unity.Jobs;

	/// <summary>
	///   Scheduler for the basic Surface Nets algorithm without materials or smoothing.
	///   This is the fastest meshing algorithm, suitable for collision meshes.
	/// </summary>
	[BurstCompile]
	public struct NaiveSurfaceNetsScheduler : IMeshingAlgorithmScheduler
	{
		public JobHandle Schedule(
			in MeshingInputData input,
			in MeshingOutputData output,
			JobHandle inputDeps
		)
		{
			var job = new NaiveSurfaceNets
			{
				edgeTable = input.edgeTable,
				volume = input.volume,
				materials = input.materials,
				buffer = output.buffer,
				indices = output.indices,
				vertices = output.vertices,
				bounds = output.bounds,
				normalsMode = input.normalsMode,
				voxelSize = input.voxelSize,
				materialDistributionMode = input.materialDistributionMode,
			};

			return job.Schedule(inputDeps);
		}
	}
}
