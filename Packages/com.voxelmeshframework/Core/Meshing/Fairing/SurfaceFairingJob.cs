namespace Voxels.Core.Meshing.Fairing
{
	using System.Runtime.CompilerServices;
	using ThirdParty.SurfaceNets;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Voxels.Core;
	using Voxels.Core.Meshing;
	using static Unity.Mathematics.math;

	/// <summary>
	///   Performs surface fairing smoothing on vertices while preserving material boundaries.
	///   This job implements the A+B+I approach with precomputed neighbors, adaptive step sizing,
	///   and cell constraint enforcement for boundary-preserving surface smoothing.
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
		///   Input vertex positions from previous iteration (or initial positions).
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<float3> inPositions;

		/// <summary>
		///   Precomputed neighbor index ranges for each vertex.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<int2> neighborIndexRanges;

		/// <summary>
		///   Flattened array of neighbor vertex indices.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<int> neighborIndices;

		/// <summary>
		///   Material IDs for each vertex (legacy). Not used for blending, kept for compatibility.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<byte> materialId;

		/// <summary>
		///   Blended material weights per vertex (RGBA -> up to 4 weights). Used for boundary detection.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<float4> materialWeights;

		/// <summary>
		///   Cell coordinates for each vertex (for constraint enforcement).
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<int3> cellCoords;

		/// <summary>
		///   Output smoothed vertex positions.
		/// </summary>
		[NoAlias]
		public NativeList<float3> outPositions;

		/// <summary>
		///   Size of each voxel in world units.
		/// </summary>
		[ReadOnly]
		public float voxelSize;

		/// <summary>
		///   Cell margin to prevent vertices from reaching cell boundaries.
		/// </summary>
		[ReadOnly]
		public float cellMargin;

		/// <summary>
		///   Base step size for fairing smoothing.
		/// </summary>
		[ReadOnly]
		public float fairingStepSize;

		// Seam constraints
		[ReadOnly]
		public SeamConstraintMode seamConstraintMode;

		[ReadOnly]
		public float seamConstraintWeight;

		[ReadOnly]
		public int seamBandWidth;

		/// <summary>
		///   Input vertices to get count from.
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

				// ===== ADAPTIVE STEP SIZE (WEIGHT DIVERGENCE + SEAM) =====
				// Reduce step size near material transitions; apply seam constraint if in seam band.
				var adaptiveStep = GetAdaptiveStepSize(vertexIndex, neighborRange);
				adaptiveStep = ApplySeamConstraint(adaptiveStep, cellCoord);

				// ===== FAIRING STEP =====
				// Move toward neighbor average: p' = p + α * (avg - p)
				var rawDelta = neighborAverage - currentPos;
				var newDelta = ApplySeamProjection(adaptiveStep * rawDelta, cellCoord);
				var newPos = currentPos + newDelta;

				// ===== CELL CONSTRAINT ENFORCEMENT =====
				// Clamp vertex to original cell bounds with scaled margin.
				newPos = ApplyCellConstraints(newPos, cellCoord);

				// Store the smoothed position
				outPositions[vertexIndex] = newPos;
			}
		}

		/// <summary>
		///   Calculates the average position of face neighbors.
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
		///   Determines adaptive step size based on material boundary detection.
		/// </summary>
		float GetAdaptiveStepSize(int vertexIndex, int2 neighborRange)
		{
			var baseStep = fairingStepSize;
			var wi = materialWeights[vertexIndex];
			var dMax = 0.0f;
			for (var i = 0; i < neighborRange.y; i++)
			{
				var neighborIndex = neighborIndices[neighborRange.x + i];
				var wj = materialWeights[neighborIndex];
				// L1 distance across weights (sum of absolute differences)
				var d = abs(wi.x - wj.x) + abs(wi.y - wj.y) + abs(wi.z - wj.z) + abs(wi.w - wj.w);
				dMax = max(dMax, d);
			}

			// Map divergence to attenuation beta in [0,1]
			const float t0 = 0.15f; // start attenuating
			const float t1 = 0.35f; // full attenuation region
			var betaDiv = 1.0f - saturate((dMax - t0) / (t1 - t0));

			// Optional confidence term: maxMinusSecondMax(w)
			var s0 = max(wi.x, max(wi.y, max(wi.z, wi.w)));
			var s1 = min(
				max(wi.x, max(wi.y, wi.z)),
				max(min(wi.x, wi.y), max(min(wi.x, wi.z), min(wi.y, wi.z)))
			); // fast second-max approx not trivial; do explicit
			// Compute exact second max explicitly
			var a = wi.x;
			var b = wi.y;
			var c = wi.z;
			var d2 = wi.w;
			var m1 = max(a, max(b, max(c, d2)));
			var m2 = max(
				min(a, b),
				max(min(a, c), max(min(a, d2), max(min(b, c), max(min(b, d2), min(c, d2)))))
			);
			var conf = max(0.0f, m1 - m2);
			const float cRef = 0.4f;
			var betaConf = saturate(conf / cRef);

			var beta = min(betaDiv, betaConf);
			return baseStep * beta;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		float3 ApplySeamProjection(float3 delta, int3 cellCoord)
		{
			if (seamConstraintMode == SeamConstraintMode.None)
				return delta;

			var inLowX = cellCoord.x < seamBandWidth;
			var inHighX = cellCoord.x >= (VoxelConstants.CHUNK_SIZE - seamBandWidth);
			var inLowY = cellCoord.y < seamBandWidth;
			var inHighY = cellCoord.y >= (VoxelConstants.CHUNK_SIZE - seamBandWidth);
			var inLowZ = cellCoord.z < seamBandWidth;
			var inHighZ = cellCoord.z >= (VoxelConstants.CHUNK_SIZE - seamBandWidth);

			var inAnyBand = inLowX || inHighX || inLowY || inHighY || inLowZ || inHighZ;
			if (!inAnyBand)
				return delta;

			if (seamConstraintMode == SeamConstraintMode.Freeze)
				return float3(0f, 0f, 0f);

			// SoftBand: block movement orthogonal to seam planes
			var result = delta;
			if (inLowX || inHighX)
				result.x = 0f;
			if (inLowY || inHighY)
				result.y = 0f;
			if (inLowZ || inHighZ)
				result.z = 0f;
			return result;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		float ApplySeamConstraint(float step, int3 cellCoord)
		{
			if (seamConstraintMode == SeamConstraintMode.None)
				return step;

			var inLowX = cellCoord.x < seamBandWidth;
			var inHighX = cellCoord.x >= (VoxelConstants.CHUNK_SIZE - seamBandWidth);
			var inLowY = cellCoord.y < seamBandWidth;
			var inHighY = cellCoord.y >= (VoxelConstants.CHUNK_SIZE - seamBandWidth);
			var inLowZ = cellCoord.z < seamBandWidth;
			var inHighZ = cellCoord.z >= (VoxelConstants.CHUNK_SIZE - seamBandWidth);
			var inAnyBand = inLowX || inHighX || inLowY || inHighY || inLowZ || inHighZ;

			if (!inAnyBand)
				return step;

			switch (seamConstraintMode)
			{
				case SeamConstraintMode.Freeze:
					return 0f;
				case SeamConstraintMode.SoftBand:
					return step * seamConstraintWeight;
				default:
					return step;
			}
		}

		/// <summary>
		///   Applies cell constraints to keep vertices within their original cell bounds.
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
				((cellCoord + new float3(1.0f, 1.0f, 1.0f)) * voxelSize)
				- new float3(scaledMargin, scaledMargin, scaledMargin);

			// ===== POSITION CLAMPING =====
			// Enforce constraints by clamping to the allowed region.
			return clamp(position, cellMin, cellMax);
		}
	}
}
