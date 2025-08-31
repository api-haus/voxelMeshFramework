namespace Voxels.Core.Meshing.Fairing
{
	using ThirdParty.SurfaceNets;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Jobs;
	using Unity.Mathematics;
	using static Unity.Mathematics.math;
	using static Voxels.Core.VoxelConstants;

	/// <summary>
	/// Performs surface fairing smoothing on vertices while preserving material boundaries.
	/// This job implements the A+B+I approach with precomputed neighbors, adaptive step sizing,
	/// and cell constraint enforcement for boundary-preserving surface smoothing.
	/// </summary>
	[BurstCompile(
		Debug = false,
		FloatMode = FloatMode.Fast,
		OptimizeFor = OptimizeFor.Performance,
		FloatPrecision = FloatPrecision.Low,
		DisableSafetyChecks = true,
		CompileSynchronously = true
	)]
	public struct SurfaceFairingJob : IJob
	{
		/// <summary>
		/// Input vertex positions from previous iteration (or initial positions).
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<float3> inPositions;

		/// <summary>
		/// Precomputed neighbor index ranges for each vertex.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<int2> neighborIndexRanges;

		/// <summary>
		/// Flattened array of neighbor vertex indices.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<int> neighborIndices;

		/// <summary>
		/// Material IDs for each vertex (from vertex.color.r).
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<byte> materialId;

		/// <summary>
		/// Cell coordinates for each vertex (for constraint enforcement).
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<int3> cellCoords;

		/// <summary>
		/// Output smoothed vertex positions.
		/// </summary>
		[NoAlias]
		public NativeList<float3> outPositions;

		/// <summary>
		/// Size of each voxel in world units.
		/// </summary>
		[ReadOnly]
		public float voxelSize;

		/// <summary>
		/// Cell margin to prevent vertices from reaching cell boundaries.
		/// </summary>
		[ReadOnly]
		public float cellMargin;

		/// <summary>
		/// Base step size for fairing smoothing.
		/// </summary>
		[ReadOnly]
		public float fairingStepSize;

		/// <summary>
		/// Input vertices to get count from.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<Vertex> vertices;

		public void Execute()
		{
			var vertexCount = vertices.Length;

			// ===== RESIZE OUTPUT LIST =====
			outPositions.ResizeUninitialized(vertexCount);

			for (var vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
			{
				var currentPos = inPositions[vertexIndex];
				var currentMaterial = materialId[vertexIndex];
				var cellCoord = cellCoords[vertexIndex];

				// ===== NEIGHBOR AVERAGING =====
				// Calculate the average position of face neighbors.
				var neighborRange = neighborIndexRanges[vertexIndex];
				var neighborAverage = CalculateNeighborAverage(vertexIndex, neighborRange, currentPos);

				// ===== ADAPTIVE STEP SIZE =====
				// Reduce step size near material boundaries to preserve sharp features.
				var adaptiveStep = GetAdaptiveStepSize(currentMaterial, neighborRange);

				// ===== FAIRING STEP =====
				// Move toward neighbor average: p' = p + α * (avg - p)
				var newPos = currentPos + (adaptiveStep * (neighborAverage - currentPos));

				// ===== CELL CONSTRAINT ENFORCEMENT =====
				// Clamp vertex to original cell bounds with scaled margin.
				newPos = ApplyCellConstraints(newPos, cellCoord);

				// Store the smoothed position
				outPositions[vertexIndex] = newPos;
			}
		}

		/// <summary>
		/// Calculates the average position of face neighbors.
		/// </summary>
		float3 CalculateNeighborAverage(int vertexIndex, int2 neighborRange, float3 fallbackPos)
		{
			var sum = new float3(0, 0, 0);
			var count = 0;

			// Sum all neighbor positions
			for (var i = 0; i < neighborRange.y; i++)
			{
				var neighborIndex = neighborIndices[neighborRange.x + i];
				sum += inPositions[neighborIndex];
				count++;
			}

			// Return average, or fallback to current position if no neighbors
			return count > 0 ? sum / count : fallbackPos;
		}

		/// <summary>
		/// Determines adaptive step size based on material boundary detection.
		/// </summary>
		float GetAdaptiveStepSize(byte currentMaterial, int2 neighborRange)
		{
			var baseStep = fairingStepSize;

			// ===== MATERIAL BOUNDARY DETECTION =====
			// Check if any face neighbor has different material.
			for (var i = 0; i < neighborRange.y; i++)
			{
				var neighborIndex = neighborIndices[neighborRange.x + i];
				var neighborMaterial = materialId[neighborIndex];

				if (neighborMaterial != currentMaterial)
				{
					// ===== BOUNDARY STEP REDUCTION =====
					// Significantly reduce step size at material boundaries
					// to preserve sharp material transitions per the paper.
					return baseStep * 0.3f;
				}
			}

			return baseStep;
		}

		/// <summary>
		/// Applies cell constraints to keep vertices within their original cell bounds.
		/// </summary>
		float3 ApplyCellConstraints(float3 position, int3 cellCoord)
		{
			// ===== SCALED MARGIN CALCULATION =====
			// Scale cell margin by voxel size to maintain consistent behavior.
			var scaledMargin = cellMargin * voxelSize;

			// ===== CELL BOUNDS =====
			// Define the constrained region within the cell.
			var cellMin =
				((float3)cellCoord * voxelSize) + new float3(scaledMargin, scaledMargin, scaledMargin);
			var cellMax =
				(((float3)cellCoord + new float3(1.0f, 1.0f, 1.0f)) * voxelSize)
				- new float3(scaledMargin, scaledMargin, scaledMargin);

			// ===== POSITION CLAMPING =====
			// Enforce constraints by clamping to the allowed region.
			return clamp(position, cellMin, cellMax);
		}
	}
}
