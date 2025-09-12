namespace Voxels.Core.Meshing.Algorithms
{
	using ThirdParty.SurfaceNets;
	using ThirdParty.SurfaceNets.Utils;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Mathematics.Geometry;

	/// <summary>
	///   Output data containers for meshing algorithms.
	/// </summary>
	[BurstCompile]
	public struct MeshingOutputData
	{
		/// <summary>
		///   Output vertex data.
		/// </summary>
		[NoAlias]
		public NativeList<Vertex> vertices;

		/// <summary>
		///   Output triangle indices.
		/// </summary>
		[NoAlias]
		public NativeList<int> indices;

		/// <summary>
		///   Temporary buffer for vertex connectivity.
		/// </summary>
		[NoAlias]
		public NativeArray<int> buffer;

		/// <summary>
		///   Output mesh bounds.
		/// </summary>
		[NoAlias]
		public UnsafePointer<MinMaxAABB> bounds;
	}
}
