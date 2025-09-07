namespace Voxels.Core.Atlasing.Hybrid
{
	using Unity.Entities;
	using UnityEngine;

	/// <summary>
	///   Per-grid settings for hybrid chunk instantiation.
	///   Holds managed references to the prefab and default material.
	/// </summary>
	public class ChunkPrefabSettings : IComponentData
	{
		public GameObject prefab;
	}
}
