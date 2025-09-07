namespace Voxels.Core.Authoring
{
	using Meshing.Algorithms;
	using Procedural;
	using Unity.Mathematics;
	using UnityEngine;
	using static Hybrid.VoxelEntityBridge;
	using static Unity.Mathematics.math;
	using static VoxelConstants;

	public sealed class VoxelMeshAtlas : MonoBehaviour
	{
		[Header("Grid Params")]
		[SerializeField]
		internal bool transformAttachment;

		[SerializeField]
		internal GameObject chunkPrefab;

		[SerializeField]
		[Min(0.1f)]
		internal float voxelSize = 1f;

		[SerializeField]
		internal Bounds gridBounds;

		[SerializeField]
		internal ProceduralVoxelGeneratorBehaviour proceduralGenerator;

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

		void OnEnable()
		{
			this.CreateAtlasEntity(gameObject.GetInstanceID(), transformAttachment ? transform : null);
		}

		void OnDisable()
		{
			DestroyEntityByInstanceID(gameObject.GetInstanceID());
		}

		void OnDrawGizmos()
		{
			Gizmos.matrix = transform.localToWorldMatrix;

			Gizmos.color = Color.black;
			Gizmos.DrawWireCube(gridBounds.center, gridBounds.size);
		}

		void OnDrawGizmosSelected()
		{
			Gizmos.matrix = transform.localToWorldMatrix;

			float3 volumeSize = gridBounds.size;

			var chunkSize = voxelSize * EFFECTIVE_CHUNK_SIZE;
			var gridDims = (int3)ceil(volumeSize / chunkSize);

			// Clamp to at least 1 in each axis to avoid zero-chunk grids when bounds are degenerate
			gridDims = max(gridDims, new int3(1));

			// Clamp to 64 max, so that we don't overwhelm gizmos rendering
			gridDims = min(64, gridDims);

			for (var x = 0; x < gridDims.x; x++)
			for (var y = 0; y < gridDims.y; y++)
			for (var z = 0; z < gridDims.z; z++)
			{
				int3 c = new(x, y, z);

				var center = (c + (float3)0.5f) * chunkSize;
				Gizmos.DrawWireCube((float3)gridBounds.min + center, (float3)chunkSize);
			}
		}
	}
}
