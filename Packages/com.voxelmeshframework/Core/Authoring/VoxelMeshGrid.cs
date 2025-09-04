namespace Voxels.Core.Authoring
{
	using Meshing;
	using Procedural;
	using Unity.Mathematics;
	using UnityEngine;
	using static Unity.Mathematics.math;
	using static VoxelConstants;
	using static VoxelEntityBridge;

	public sealed class VoxelMeshGrid : MonoBehaviour
	{
		[SerializeField]
		internal bool transformAttachment;

		[SerializeField]
		internal Material surfaceMaterial;

		[SerializeField]
		internal GameObject chunkPrefab;

		[SerializeField]
		[Min(0.1f)]
		internal float voxelSize = 1f;

		[SerializeField]
		internal Bounds worldBounds;

		[SerializeField]
		internal ProceduralVoxelGeneratorBehaviour procedural;

		[Header("Meshing Algorithm")]
		[SerializeField]
		internal VoxelMeshingAlgorithm meshingAlgorithm = VoxelMeshingAlgorithm.NAIVE_SURFACE_NETS;

		[Header("Surface Fairing Settings")]
		[SerializeField]
		internal bool enableFairing;

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
		internal MaterialDistributionMode materialDistributionMode =
			MaterialDistributionMode.BLENDED_CORNER_SUM;

		void Awake()
		{
			this.CreateVoxelMeshGridEntity(
				gameObject.GetInstanceID(),
				transformAttachment ? transform : null
			);
		}

		void OnDestroy()
		{
			DestroyEntityByInstanceID(gameObject.GetInstanceID());
		}

		void OnDrawGizmos()
		{
			if (transformAttachment)
				Gizmos.matrix = transform.localToWorldMatrix;

			Gizmos.color = Color.black;
			Gizmos.DrawWireCube(worldBounds.center, worldBounds.size);
		}

		void OnDrawGizmosSelected()
		{
			if (transformAttachment)
				Gizmos.matrix = transform.localToWorldMatrix;

			float3 volumeSize = worldBounds.size;

			var chunkSize = voxelSize * EFFECTIVE_CHUNK_SIZE;
			var chunks = (int3)ceil(volumeSize / chunkSize);

			chunks = min(64, chunks);

			for (var x = 0; x < chunks.x; x++)
			for (var y = 0; y < chunks.y; y++)
			for (var z = 0; z < chunks.z; z++)
			{
				int3 c = new(x, y, z);

				var center = (c + (float3)0.5f) * chunkSize;
				Gizmos.DrawWireCube((float3)worldBounds.min + center, (float3)chunkSize);
			}
		}
	}
}
