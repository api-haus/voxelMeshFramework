namespace Voxels.Core.Meshing.Fairing
{
	using ThirdParty.SurfaceNets;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Jobs;
	using Unity.Mathematics;

	/// <summary>
	/// Extracts position and material data from vertices for fairing processing.
	/// </summary>
	[BurstCompile(
		Debug = false,
		FloatMode = FloatMode.Fast,
		OptimizeFor = OptimizeFor.Performance,
		FloatPrecision = FloatPrecision.Low,
		DisableSafetyChecks = true,
		CompileSynchronously = true
	)]
	public struct ExtractVertexDataJob : IJob
	{
		[NoAlias]
		[ReadOnly]
		public NativeList<Vertex> vertices;

		[NoAlias]
		public NativeList<float3> outPositions;

		[NoAlias]
		public NativeList<byte> outMaterialIds;

		public void Execute()
		{
			var vertexCount = vertices.Length;

			// ===== RESIZE OUTPUT LISTS =====
			outPositions.ResizeUninitialized(vertexCount);
			outMaterialIds.ResizeUninitialized(vertexCount);

			for (var index = 0; index < vertexCount; index++)
			{
				var vertex = vertices[index];
				outPositions[index] = vertex.position;
				outMaterialIds[index] = vertex.color.r;
			}
		}
	}

	/// <summary>
	/// Copies positions from one array to another.
	/// </summary>
	[BurstCompile(
		Debug = false,
		FloatMode = FloatMode.Fast,
		OptimizeFor = OptimizeFor.Performance,
		FloatPrecision = FloatPrecision.Low,
		DisableSafetyChecks = true,
		CompileSynchronously = true
	)]
	public struct CopyPositionsJob : IJob
	{
		[NoAlias]
		[ReadOnly]
		public NativeArray<float3> source;

		[NoAlias]
		[WriteOnly]
		public NativeArray<float3> destination;

		/// <summary>
		/// Number of positions to copy.
		/// </summary>
		[ReadOnly]
		public int vertexCount;

		public void Execute()
		{
			for (var index = 0; index < vertexCount; index++)
			{
				destination[index] = source[index];
			}
		}
	}

	/// <summary>
	/// Updates vertex positions with new fairing results.
	/// </summary>
	[BurstCompile(
		Debug = false,
		FloatMode = FloatMode.Fast,
		OptimizeFor = OptimizeFor.Performance,
		FloatPrecision = FloatPrecision.Low,
		DisableSafetyChecks = true,
		CompileSynchronously = true
	)]
	public struct UpdateVertexPositionsJob : IJob
	{
		[NoAlias]
		public NativeList<Vertex> vertices;

		[NoAlias]
		[ReadOnly]
		public NativeList<float3> newPositions;

		public void Execute()
		{
			var vertexCount = vertices.Length;
			for (var index = 0; index < vertexCount; index++)
			{
				var vertex = vertices[index];
				vertex.position = newPositions[index];
				vertices[index] = vertex;
			}
		}
	}

	/// <summary>
	/// Updates vertex normals with recalculated values.
	/// </summary>
	[BurstCompile(
		Debug = false,
		FloatMode = FloatMode.Fast,
		OptimizeFor = OptimizeFor.Performance,
		FloatPrecision = FloatPrecision.Low,
		DisableSafetyChecks = true,
		CompileSynchronously = true
	)]
	public struct UpdateVertexNormalsJob : IJob
	{
		[NoAlias]
		public NativeList<Vertex> vertices;

		[NoAlias]
		[ReadOnly]
		public NativeList<float3> newNormals;

		public void Execute()
		{
			var vertexCount = vertices.Length;
			for (var index = 0; index < vertexCount; index++)
			{
				var vertex = vertices[index];
				vertex.normal = newNormals[index];
				vertices[index] = vertex;
			}
		}
	}

	/// <summary>
	/// Clears normals array to zero before accumulation.
	/// </summary>
	[BurstCompile(
		Debug = false,
		FloatMode = FloatMode.Fast,
		OptimizeFor = OptimizeFor.Performance,
		FloatPrecision = FloatPrecision.Low,
		DisableSafetyChecks = true,
		CompileSynchronously = true
	)]
	public struct ClearNormalsJob : IJob
	{
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

			// ===== RESIZE AND CLEAR NORMALS =====
			normals.ResizeUninitialized(vertexCount);
			for (var index = 0; index < vertexCount; index++)
			{
				normals[index] = new float3(0, 0, 0);
			}
		}
	}
}
