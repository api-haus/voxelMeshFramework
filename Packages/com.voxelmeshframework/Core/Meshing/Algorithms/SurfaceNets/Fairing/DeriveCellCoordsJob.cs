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
	///   Derives cell coordinates for vertices based on their positions.
	///   This job computes the 3D cell coordinates and linear cell indices
	///   for each vertex position within the chunk.
	/// </summary>
	[BurstCompile(
		Debug = false,
		FloatMode = FloatMode.Fast,
		OptimizeFor = OptimizeFor.Performance,
		FloatPrecision = FloatPrecision.Low,
		DisableSafetyChecks = true,
		CompileSynchronously = true
	)]
	public struct DeriveCellCoordsJob : IJob
	{
		/// <summary>
		///   Input vertex positions in world space.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<float3> positions;

		/// <summary>
		///   Size of each voxel in world units.
		/// </summary>
		[ReadOnly]
		public float voxelSize;

		/// <summary>
		///   Output cell coordinates for each vertex.
		/// </summary>
		[NoAlias]
		public NativeList<int3> cellCoords;

		/// <summary>
		///   Output linear cell indices for each vertex.
		/// </summary>
		[NoAlias]
		public NativeList<int> cellLinearIndex;

		/// <summary>
		///   Input vertices to get count from.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<Vertex> vertices;

		public void Execute()
		{
			var vertexCount = vertices.Length;

			// ===== RESIZE OUTPUT LISTS =====
			cellCoords.ResizeUninitialized(vertexCount);
			cellLinearIndex.ResizeUninitialized(vertexCount);

			for (var index = 0; index < vertexCount; index++)
			{
				// ===== CELL COORDINATE CALCULATION =====
				// Convert world position to cell coordinates within the chunk.
				// Add small epsilon to handle floating point precision issues.
				var position = positions[index];
				var cellCoord = (int3)floor((position / voxelSize) + 1e-6f);

				// ===== BOUNDS CLAMPING =====
				// Ensure cell coordinates are within valid chunk bounds [0, CHUNK_SIZE-1].
				cellCoord = clamp(
					cellCoord,
					new int3(0, 0, 0),
					new int3(CHUNK_SIZE - 1, CHUNK_SIZE - 1, CHUNK_SIZE - 1)
				);

				// ===== LINEAR INDEX CALCULATION =====
				// Convert 3D cell coordinates to linear index for dense array access.
				// Use standard chunk indexing: index = x*CS*CS + y*CS + z
				var linearIndex =
					(cellCoord.x * CHUNK_SIZE * CHUNK_SIZE) + (cellCoord.y * CHUNK_SIZE) + cellCoord.z;

				// Store results
				cellCoords[index] = cellCoord;
				cellLinearIndex[index] = linearIndex;
			}
		}
	}
}
