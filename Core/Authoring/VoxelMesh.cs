namespace Voxels.Core.Authoring
{
	using Hybrid;
	using Unity.Mathematics;
	using UnityEngine;
	using static Hybrid.VoxelEntityBridge;
	using static VoxelConstants;

	[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
	public sealed class VoxelMesh : MonoBehaviour
	{
		[SerializeField]
		internal VoxelMeshingConfig config;

		void Awake()
		{
			this.CreateVoxelMeshEntity(
				gameObject.GetInstanceID(),
				config.transformAttachment ? transform : null
			);
		}

		void OnDestroy()
		{
			DestroyEntityByInstanceID(gameObject.GetInstanceID());
		}

		void OnDrawGizmosSelected()
		{
			TryGetComponent(out MeshRenderer mr);

			var b = mr.bounds;
			Gizmos.color = Color.black;
			Gizmos.DrawWireCube(b.center, b.size);

			Gizmos.matrix = transform.localToWorldMatrix;

			b = mr.localBounds;
			Gizmos.color = Color.blue;
			Gizmos.DrawWireCube(b.center, b.size);

			float3 size = config.voxelSize * CHUNK_SIZE;

			Gizmos.color = Color.white;
			Gizmos.DrawWireCube(size * .5f, size);
		}
	}
}
