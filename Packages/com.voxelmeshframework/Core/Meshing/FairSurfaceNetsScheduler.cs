namespace Voxels.Core.Meshing
{
	using ThirdParty.SurfaceNets;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Jobs;

	/// <summary>
	/// Scheduler for Surface Nets with surface fairing and material support.
	/// Produces smoother meshes while preserving sharp features and material boundaries.
	/// </summary>
	[BurstCompile]
	public struct FairSurfaceNetsScheduler : IMeshingAlgorithmScheduler
	{
		public int fairingIterations;
		public float fairingStepSize;
		public float cellMargin;

		public JobHandle Schedule(
			in MeshingInputData input,
			in MeshingOutputData output,
			JobHandle inputDeps
		)
		{
			// Allocate vertex cell coordinates for fairing
			var vertexCellCoords = new NativeList<Unity.Mathematics.int3>(Allocator.TempJob);

			var job = new FairSurfaceNets
			{
				edgeTable = input.edgeTable,
				volume = input.volume,
				materials = input.materials,
				buffer = output.buffer,
				indices = output.indices,
				vertices = output.vertices,
				vertexCellCoords = vertexCellCoords,
				bounds = output.bounds,
				recalculateNormals = input.recalculateNormals,
				voxelSize = input.voxelSize,

				// Fairing parameters
				enableSurfaceFairing = fairingIterations > 0,
				fairingIterations = fairingIterations,
				fairingStepSize = fairingStepSize,
				cellMargin = cellMargin,
			};

			var handle = job.Schedule(inputDeps);

			// Schedule disposal of temporary data
			vertexCellCoords.Dispose(handle);

			return handle;
		}
	}
}
