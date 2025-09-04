namespace Voxels.Core.Grids
{
	using Unity.Entities;
	using UnityEngine;

	/// <summary>
	///   Per-grid settings for hybrid chunk instantiation.
	///   Holds managed references to the prefab and default material.
	/// </summary>
	public class ChunkPrefabSettings : IComponentData
	{
		public Material defaultMaterial;
		public GameObject prefab;
	}
}
