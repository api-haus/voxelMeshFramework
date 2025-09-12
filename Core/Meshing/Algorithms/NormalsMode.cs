namespace Voxels.Core.Meshing.Algorithms
{
	using UnityEngine;

	/// <summary>
	///   Controls how vertex normals are populated during meshing.
	/// </summary>
	public enum NormalsMode : byte
	{
		/// <summary>
		///   Do not compute normals in the base meshing job. Useful when a later pass will recompute normals.
		/// </summary>
		[InspectorName("Do not compute normals")]
		NONE = 0,

		/// <summary>
		///   Compute normals from the voxel field gradient during vertex generation (fast, approximate).
		/// </summary>
		[InspectorName("Compute normals from SDF gradient (faster)")]
		GRADIENT = 1,

		/// <summary>
		///   Compute normals from triangle geometry after indices are produced (higher quality, slower).
		/// </summary>
		[InspectorName("Compute normals from triangle geometry (accurate, smooth)")]
		TRIANGLE_GEOMETRY = 2,
	}
}
