namespace Voxels.Core.Meshing.Algorithms
{
	using Unity.Burst;
	using Unity.Collections;

	/// <summary>
	///   Input data required for all meshing algorithms.
	/// </summary>
	[BurstCompile]
	public struct MeshingInputData
	{
		/// <summary>
		///   Signed distance field volume data.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeArray<sbyte> volume;

		/// <summary>
		///   Material IDs for each voxel (optional).
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeArray<byte> materials;

		/// <summary>
		///   Edge table for Surface Nets algorithms.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeArray<ushort> edgeTable;

		/// <summary>
		///   Size of each voxel in world units.
		/// </summary>
		public float voxelSize;

		public float positionJitter;

		/// <summary>
		///   Size of the chunk (must be 32 for SIMD optimizations).
		/// </summary>
		public int chunkSize;

		/// <summary>
		///   Normal computation strategy for the base meshing job.
		/// </summary>
		public NormalsMode normalsMode;

		/// <summary>
		///   How to distribute materials to vertices.
		/// </summary>
		public MaterialEncoding materialEncoding;
	}
}
