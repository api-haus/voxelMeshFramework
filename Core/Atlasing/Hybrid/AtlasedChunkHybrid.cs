namespace Voxels.Core.Atlasing.Hybrid
{
	using Unity.Entities;
	using UnityEngine;

	/// <summary>
	///   Marks a chunk entity whose Hybrid GameObject has been instantiated and attached.
	/// </summary>
	public struct AtlasedChunkHybrid : ICleanupComponentData
	{
		public UnityObjectRef<GameObject> chunkGameObject;
		public UnityObjectRef<GameObject> gridGameObject;
	}
}
