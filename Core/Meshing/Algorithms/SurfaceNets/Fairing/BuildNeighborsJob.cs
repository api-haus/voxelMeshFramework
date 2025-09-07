namespace Voxels.Core.Meshing.Algorithms.SurfaceNets.Fairing
{
	using ThirdParty.SurfaceNets;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Jobs;
	using Unity.Mathematics;
	using static Unity.Mathematics.math;
	using static VoxelConstants;

	/// <summary>
	///   Builds precomputed neighbor adjacency data for efficient surface fairing.
	///   This job constructs face-neighbor relationships using the dense cell map
	///   for O(1) neighbor lookups during fairing iterations.
	/// </summary>
	[BurstCompile(
		Debug = false,
		FloatMode = FloatMode.Fast,
		OptimizeFor = OptimizeFor.Performance,
		FloatPrecision = FloatPrecision.Low,
		DisableSafetyChecks = true,
		CompileSynchronously = true
	)]
	public struct BuildNeighborsJob : IJob
	{
		/// <summary>
		///   Input cell coordinates for each vertex.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<int3> cellCoords;

		/// <summary>
		///   Input dense cell-to-vertex mapping.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeArray<int> cellToVertex;

		/// <summary>
		///   Output neighbor index ranges (start, count) for each vertex.
		/// </summary>
		[NoAlias]
		public NativeList<int2> neighborIndexRanges;

		/// <summary>
		///   Output flattened array of neighbor vertex indices.
		/// </summary>
		[NoAlias]
		public NativeList<int> neighborIndices;

		/// <summary>
		///   Input vertices to get count from.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<Vertex> vertices;

		public void Execute()
		{
			// ===== FACE NEIGHBOR OFFSETS =====
			// Define the 6 face directions for neighbor detection.
			// Use stack allocation for best performance.
			unsafe
			{
				var faceOffsets = stackalloc int3[6];
				faceOffsets[0] = new int3(-1, 0, 0); // -X
				faceOffsets[1] = new int3(1, 0, 0); // +X
				faceOffsets[2] = new int3(0, -1, 0); // -Y
				faceOffsets[3] = new int3(0, 1, 0); // +Y
				faceOffsets[4] = new int3(0, 0, -1); // -Z
				faceOffsets[5] = new int3(0, 0, 1); // +Z

				var vertexCount = vertices.Length;

				// ===== RESIZE OUTPUT LISTS =====
				neighborIndexRanges.ResizeUninitialized(vertexCount);
				neighborIndices.Clear(); // Will grow dynamically

				// Process all vertices sequentially
				for (var vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
				{
					var cellCoord = cellCoords[vertexIndex];
					var startIndex = neighborIndices.Length; // Current end becomes start for this vertex

					// ===== NEIGHBOR COLLECTION =====
					// Find valid face neighbors using the dense cell map.
					for (var dir = 0; dir < 6; dir++)
					{
						var neighborCell = cellCoord + faceOffsets[dir];

						// ===== BOUNDS CHECK =====
						// Ensure neighbor cell is within valid chunk bounds.
						if (all(neighborCell >= 0) && all(neighborCell < CHUNK_SIZE))
						{
							// ===== SEAM SLAB RESTRICTION =====
							// If the source cell lies within any overlap slab along an axis,
							// restrict neighbors to remain within the same slab for those axes.
							if (!SeamUtils.NeighborRespectsSeamSlab(cellCoord, neighborCell))
								continue;

							// ===== CELL-TO-VERTEX LOOKUP =====
							// Convert 3D neighbor coordinates to linear index and look up vertex.
							var neighborLinearIndex =
								(neighborCell.x * CHUNK_SIZE * CHUNK_SIZE)
								+ (neighborCell.y * CHUNK_SIZE)
								+ neighborCell.z;

							var neighborVertex = cellToVertex[neighborLinearIndex];

							// ===== VALID NEIGHBOR CHECK =====
							// Only include cells that actually contain vertices.
							if (neighborVertex >= 0)
								neighborIndices.Add(neighborVertex);
						}
					}

					// ===== STORE NEIGHBOR RANGE =====
					// Record the start position and count for this vertex's neighbors.
					var neighborCount = neighborIndices.Length - startIndex;
					neighborIndexRanges[vertexIndex] = new int2(startIndex, neighborCount);
				}
			}
		}
	}
}
