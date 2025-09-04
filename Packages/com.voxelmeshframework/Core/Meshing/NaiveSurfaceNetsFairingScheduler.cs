namespace Voxels.Core.Meshing
{
	using Fairing;
	using Unity.Burst;
	using Unity.Jobs;

	/// <summary>
	///   Scheduler that runs NaiveSurfaceNets then applies the fairing post-process pipeline using preallocated buffers.
	/// </summary>
	[BurstCompile]
	public struct NaiveSurfaceNetsFairingScheduler : IMeshingAlgorithmScheduler
	{
		public FairingBuffers fairingBuffers;
		public int fairingIterations;
		public float fairingStepSize;
		public float cellMargin;
		public bool recomputeNormalsAfterFairing;
		public SeamConstraintMode seamConstraintMode;
		public float seamConstraintWeight;
		public int seamBandWidth;

		public JobHandle Schedule(
			in MeshingInputData input,
			in MeshingOutputData output,
			JobHandle inputDeps
		)
		{
			// base meshing
			var meshingJob = new NaiveSurfaceNetsScheduler().Schedule(input, output, inputDeps);

			// extract attributes for fairing
			meshingJob = new ExtractVertexDataJob
			{
				vertices = output.vertices,
				outPositions = fairingBuffers.positionsA,
				outMaterialIds = fairingBuffers.materialIds,
				outMaterialWeights = fairingBuffers.materialWeights,
			}.Schedule(meshingJob);

			meshingJob = new DeriveCellCoordsJob
			{
				vertices = output.vertices,
				positions = fairingBuffers.positionsA,
				voxelSize = input.voxelSize,
				cellCoords = fairingBuffers.cellCoords,
				cellLinearIndex = fairingBuffers.cellLinearIndex,
			}.Schedule(meshingJob);

			meshingJob = new BuildCellToVertexMapJob
			{
				vertices = output.vertices,
				cellLinearIndex = fairingBuffers.cellLinearIndex,
				cellToVertex = fairingBuffers.cellToVertex,
			}.Schedule(meshingJob);

			meshingJob = new BuildNeighborsJob
			{
				vertices = output.vertices,
				cellCoords = fairingBuffers.cellCoords,
				cellToVertex = fairingBuffers.cellToVertex,
				neighborIndexRanges = fairingBuffers.neighborIndexRanges,
				neighborIndices = fairingBuffers.neighborIndices,
			}.Schedule(meshingJob);

			// iterations
			var usePositionsB = false;
			for (var iteration = 0; iteration < fairingIterations; iteration++)
			{
				var inBuffer = usePositionsB ? fairingBuffers.positionsB : fairingBuffers.positionsA;
				var outBuffer = usePositionsB ? fairingBuffers.positionsA : fairingBuffers.positionsB;

				meshingJob = new SurfaceFairingJob
				{
					vertices = output.vertices,
					inPositions = inBuffer,
					neighborIndexRanges = fairingBuffers.neighborIndexRanges,
					neighborIndices = fairingBuffers.neighborIndices,
					materialId = fairingBuffers.materialIds,
					materialWeights = fairingBuffers.materialWeights,
					cellCoords = fairingBuffers.cellCoords,
					outPositions = outBuffer,
					voxelSize = input.voxelSize,
					cellMargin = cellMargin,
					fairingStepSize = fairingStepSize,
					seamConstraintMode = seamConstraintMode,
					seamConstraintWeight = seamConstraintWeight,
					seamBandWidth = seamBandWidth,
				}.Schedule(meshingJob);

				usePositionsB = !usePositionsB;
			}

			// update vertices
			var finalPositions = usePositionsB ? fairingBuffers.positionsB : fairingBuffers.positionsA;
			meshingJob = new UpdateVertexPositionsJob
			{
				vertices = output.vertices,
				newPositions = finalPositions,
			}.Schedule(meshingJob);

			// optional normals recompute — single-pass in-place
			if (recomputeNormalsAfterFairing)
				meshingJob = new RecalculateNormalsJob
				{
					indices = output.indices.AsDeferredJobArray(),
					vertices = output.vertices,
				}.Schedule(meshingJob);

			return meshingJob;
		}
	}
}
