namespace Voxels.Core.Authoring
{
	using System;
	using Meshing.Algorithms;
	using Procedural;
	using UnityEngine;

	[Serializable]
	public class VoxelMeshingConfig
	{
		[SerializeField]
		internal bool transformAttachment;

		[SerializeField]
		internal bool stampEditable = true;

		[SerializeField]
		[Min(0.1f)]
		internal float voxelSize = 1f;

		[SerializeField]
		internal ProceduralVoxelGeneratorBehaviour voxelGenerator;

		[SerializeField]
		internal ProceduralMaterialGeneratorBehaviour materialGenerator;

		[Header("Meshing Algorithm")]
		[SerializeField]
		internal VoxelMeshingAlgorithm meshingAlgorithm = VoxelMeshingAlgorithm.NAIVE_SURFACE_NETS;

		[SerializeField]
		internal NormalsMode normalsMode = NormalsMode.GRADIENT;

		[Header("Materials")]
		[SerializeField]
		internal MaterialEncoding materialEncoding = MaterialEncoding.COLOR_SPLAT_4;
	}
}
