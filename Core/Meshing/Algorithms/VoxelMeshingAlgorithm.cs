namespace Voxels.Core.Meshing.Algorithms
{
	using UnityEngine;

	/// <summary>
	///   Available voxel meshing algorithms.
	///   Each algorithm has different performance and quality characteristics.
	/// </summary>
	public enum VoxelMeshingAlgorithm : byte
	{
		/// <summary>
		///   Surface Nets with material support.
		/// </summary>
		[InspectorName("Surface Nets")]
		NAIVE_SURFACE_NETS = 0,
	}
}
