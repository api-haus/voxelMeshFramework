namespace Voxels.Core.Authoring
{
	using Meshing.Algorithms;
	using Procedural;
	using Unity.Mathematics;
	using UnityEngine;
	using static Hybrid.VoxelEntityBridge;
	using static VoxelConstants;

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

		[Header("Meshing Algorithm")]
		[SerializeField]
		internal VoxelMeshingAlgorithm meshingAlgorithm = VoxelMeshingAlgorithm.NAIVE_SURFACE_NETS;

		[SerializeField]
		internal NormalsMode normalsMode = NormalsMode.GRADIENT;

		[Header("Surface Fairing Settings")]
		[SerializeField]
		[Range(0, 10)]
		internal int fairingIterations = 5;

		[SerializeField]
		[Range(0.3f, 0.8f)]
		internal float fairingStepSize = 0.6f;

		[SerializeField]
		[Range(0.05f, 0.2f)]
		internal float cellMargin = 0.1f;

		[SerializeField]
		internal bool recomputeNormalsAfterFairing;

		[Header("Materials")]
		[SerializeField]
		internal MaterialEncoding materialEncoding = MaterialEncoding.COLOR_SPLAT_4;

		void Awake()
		{
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
