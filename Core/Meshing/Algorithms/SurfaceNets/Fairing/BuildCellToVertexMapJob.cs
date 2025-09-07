namespace Voxels.Core.Meshing.Algorithms.SurfaceNets.Fairing
{
	using ThirdParty.SurfaceNets;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Jobs;
	using static VoxelConstants;

	/// <summary>
	///   Builds a dense cell-to-vertex mapping for O(1) neighbor lookups.
	///   This job creates a dense 3D array that maps each cell to its containing vertex.
	///   For Surface Nets, each cell contains at most one vertex.
	/// </summary>
	[BurstCompile(
		Debug = false,
		FloatMode = FloatMode.Fast,
		OptimizeFor = OptimizeFor.Performance,
		FloatPrecision = FloatPrecision.Low,
		DisableSafetyChecks = true,
		CompileSynchronously = true
	)]
	public struct BuildCellToVertexMapJob : IJob
	{
		/// <summary>
		///   Input linear cell indices for each vertex.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<int> cellLinearIndex;

		/// <summary>
		///   Output dense cell-to-vertex map.
		///   Size: CHUNK_SIZE^3, initialized to -1 (empty).
		/// </summary>
		[NoAlias]
		public NativeArray<int> cellToVertex;

		/// <summary>
		///   Input vertices to get count from.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<Vertex> vertices;

		public void Execute()
		{
			// ===== INITIALIZATION =====
			// Initialize the entire cell map to -1 (no vertex).
			var totalCells = CHUNK_SIZE * CHUNK_SIZE * CHUNK_SIZE;
			for (var i = 0; i < totalCells; i++)
				cellToVertex[i] = -1;

			// ===== VERTEX ASSIGNMENT =====
			// For each vertex, assign it to its owning cell.
			// Since Surface Nets guarantees one vertex per cell, we can use simple assignment.
			// If multiple vertices map to the same cell (shouldn't happen), keep the first one.
			var vertexCount = vertices.Length;
			for (var i = 0; i < vertexCount; i++)
			{
				var cellIndex = cellLinearIndex[i];

				// Only assign if cell is currently empty (first-come rule)
				if (cellToVertex[cellIndex] == -1)
					cellToVertex[cellIndex] = i;
			}
		}
	}
}
