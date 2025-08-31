namespace Voxels.Core.Meshing.Fairing
{
	using ThirdParty.SurfaceNets;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Jobs;
	using Unity.Mathematics;
	using static Unity.Mathematics.math;

	/// <summary>
	/// Recalculates vertex normals from triangle geometry after surface fairing.
	/// This job provides higher-quality normals by computing them directly from
	/// the triangle mesh geometry rather than from voxel gradients.
	/// Uses sequential processing to avoid atomic operations.
	/// </summary>
	[BurstCompile(
		Debug = false,
		FloatMode = FloatMode.Fast,
		OptimizeFor = OptimizeFor.Performance,
		FloatPrecision = FloatPrecision.Low,
		DisableSafetyChecks = true,
		CompileSynchronously = true
	)]
	public struct RecalculateNormalsJob : IJob
	{
		/// <summary>
		/// Triangle indices (every 3 indices form a triangle).
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeArray<int> indices;

		/// <summary>
		/// Input vertex positions after fairing.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeArray<float3> positions;

		/// <summary>
		/// Output recalculated normals.
		/// </summary>
		[NoAlias]
		public NativeList<float3> normals;

		/// <summary>
		/// Input vertices for position data.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<Vertex> vertices;

		/// <summary>
		/// Number of vertices to process.
		/// </summary>
		[ReadOnly]
		public int vertexCount;

		public void Execute()
		{
			// Process all triangles sequentially to avoid atomic operations
			var triangleCount = indices.Length / 6;

			for (var triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
			{
				// ===== TRIANGLE INDICES =====
				// Surface Nets generates triangles in groups of 6 indices (2 triangles forming a quad).
				// Process triangles in pairs for efficiency.
				var baseIndex = triangleIndex * 6;

				// Ensure we don't exceed the index array bounds
				if (baseIndex + 5 >= indices.Length)
					continue;

				// ===== VERTEX INDEX EXTRACTION =====
				// Extract the 4 unique vertex indices from the 6 triangle indices.
				var idx0 = indices[baseIndex + 0]; // Shared vertex
				var idx1 = indices[baseIndex + 1]; // Shared vertex
				var idx2 = indices[baseIndex + 2]; // Unique to first triangle
				var idx3 = indices[baseIndex + 4]; // Unique to second triangle

				// ===== VERTEX POSITION RETRIEVAL =====
				var pos0 = positions[idx0];
				var pos1 = positions[idx1];
				var pos2 = positions[idx2];
				var pos3 = positions[idx3];

				// ===== EDGE VECTOR CALCULATION =====
				var edge01 = pos1 - pos0; // Shared edge
				var edge02 = pos2 - pos0; // Edge to first triangle's unique vertex
				var edge03 = pos3 - pos0; // Edge to second triangle's unique vertex

				// ===== TRIANGLE NORMAL CALCULATION =====
				// Calculate face normals using cross products.
				var normal0 = cross(edge01, edge02); // First triangle normal
				var normal1 = cross(edge03, edge01); // Second triangle normal

				// ===== NaN PROTECTION =====
				// Handle degenerate triangles that produce invalid normals.
				if (any(isnan(normal0)))
					normal0 = new float3(0, 0, 0);
				if (any(isnan(normal1)))
					normal1 = new float3(0, 0, 0);

				// ===== NORMAL ACCUMULATION =====
				// Accumulate normals to vertices. Sequential processing eliminates need for atomic operations.
				// Shared vertices receive contributions from both triangles.
				normals[idx0] = normals[idx0] + normal0 + normal1;
				normals[idx1] = normals[idx1] + normal0 + normal1;
				normals[idx2] = normals[idx2] + normal0;
				normals[idx3] = normals[idx3] + normal1;
			}
		}
	}

	/// <summary>
	/// Normalizes accumulated vertex normals after triangle normal accumulation.
	/// This job must run after RecalculateNormalsJob to finalize the normals.
	/// </summary>
	[BurstCompile(
		Debug = false,
		FloatMode = FloatMode.Fast,
		OptimizeFor = OptimizeFor.Performance,
		FloatPrecision = FloatPrecision.Low,
		DisableSafetyChecks = true,
		CompileSynchronously = true
	)]
	public struct NormalizeNormalsJob : IJob
	{
		/// <summary>
		/// Accumulated normals to be normalized.
		/// </summary>
		[NoAlias]
		public NativeList<float3> normals;

		/// <summary>
		/// Input vertices to get count from.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeList<Vertex> vertices;

		public void Execute()
		{
			var vertexCount = vertices.Length;
			for (var vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
			{
				var normal = normals[vertexIndex];
				var normalLength = length(normal);

				// ===== NORMALIZATION WITH SAFETY CHECK =====
				// Avoid division by zero for zero-length normals.
				if (normalLength > 0.0001f)
					normals[vertexIndex] = normal / normalLength;
				else
					normals[vertexIndex] = new float3(0, 0, 0);
			}
		}
	}
}
