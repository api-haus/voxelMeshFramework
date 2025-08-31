namespace Voxels.Core.Authoring
{
	using Procedural;
	using Unity.Mathematics;
	using UnityEngine;
	using static VoxelConstants;
	using static VoxelEntityBridge;

	[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
	public sealed class VoxelMesh : MonoBehaviour
	{
		[SerializeField]
		internal bool transformAttachment;

		[SerializeField]
		[Min(0.1f)]
		internal float voxelSize = 1f;

		[SerializeField]
		internal ProceduralVoxelGeneratorBehaviour procedural;

		void Awake()
		{
			// TODO: work around issue when Undo in Play Mode leads to de-linking and a runtime entity error
			this.CreateVoxelMeshEntity(
				gameObject.GetInstanceID(),
				transformAttachment ? transform : null
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

			float3 size = voxelSize * CHUNK_SIZE;

			Gizmos.color = Color.white;
			Gizmos.DrawWireCube(size * .5f, size);
		}
	}
}
