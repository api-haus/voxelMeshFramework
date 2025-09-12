namespace Voxels.Core.Meshing.Algorithms
{
	using System;
	using Unity.Entities;

	/// <summary>
	///   Component that specifies which meshing algorithm to use.
	/// </summary>
	[Serializable]
	public struct VoxelMeshingAlgorithmComponent : IComponentData
	{
		public VoxelMeshingAlgorithm algorithm;
		public NormalsMode normalsMode;

		// Material distribution
		public MaterialEncoding materialEncoding;

		/// <summary>
		///   Default configuration for basic Surface Nets.
		/// </summary>
		public static VoxelMeshingAlgorithmComponent Default =>
			new()
			{
				algorithm = VoxelMeshingAlgorithm.NAIVE_SURFACE_NETS,
				normalsMode = NormalsMode.GRADIENT,
				materialEncoding = MaterialEncoding.COLOR_SPLAT_4,
			};
	}
}
