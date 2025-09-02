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
		/// Triangle indices (every 6 indices form two triangles of a quad in Surface Nets).
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeArray<int> indices;

		/// <summary>
		/// Vertices to read positions from and write normals to (in place).
		/// </summary>
		[NoAlias]
		public NativeList<Vertex> vertices;

		public void Execute()
		{
			// Accumulate face normals directly into vertex normals, then normalize in place.
			// Follows NaiveSurfaceNets.RecalculateNormals without preliminary clearing.
			var indicesLength = indices.Length;
			for (var baseIndex = 0; baseIndex + 5 < indicesLength; baseIndex += 6)
			{
				var idx0 = indices[baseIndex + 0];
				var idx1 = indices[baseIndex + 1];
				var idx2 = indices[baseIndex + 2];
				var idx3 = indices[baseIndex + 4];

				var v0 = vertices[idx0];
				var v1 = vertices[idx1];
				var v2 = vertices[idx2];
				var v3 = vertices[idx3];

				var edge01 = v1.position - v0.position;
				var edge02 = v2.position - v0.position;
				var edge03 = v3.position - v0.position;

				var normal0 = cross(edge01, edge02);
				var normal1 = cross(edge03, edge01);

				if (any(isnan(normal0)))
					normal0 = new float3(0, 0, 0);
				if (any(isnan(normal1)))
					normal1 = new float3(0, 0, 0);

				v0.normal = v0.normal + normal0 + normal1;
				v1.normal = v1.normal + normal0 + normal1;
				v2.normal = v2.normal + normal0;
				v3.normal = v3.normal + normal1;

				vertices[idx0] = v0;
				vertices[idx1] = v1;
				vertices[idx2] = v2;
				vertices[idx3] = v3;
			}

			var count = vertices.Length;
			for (var i = 0; i < count; i++)
			{
				var v = vertices[i];
				var len = length(v.normal);
				if (len > 0.0001f)
					v.normal = v.normal / len;
				else
					v.normal = new float3(0, 0, 0);
				vertices[i] = v;
			}
		}
	}
}
