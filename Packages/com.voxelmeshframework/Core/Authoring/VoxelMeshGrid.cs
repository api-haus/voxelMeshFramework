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
		internal MaterialDistributionMode materialDistributionMode =
			MaterialDistributionMode.BLENDED_CORNER_SUM;

		[Header("Rolling Grid")]
		[SerializeField]
		internal bool rolling;

		[SerializeField]
		internal bool externalDriver;

		// test hooks
		internal void __SetRolling(bool value) => rolling = value;

		internal void __SetExternalDriver(bool value) => externalDriver = value;

		internal void __SetVoxelSize(float value) => voxelSize = value;

		internal void __SetWorldBounds(Bounds b) => worldBounds = b;

		internal void __SetProcedural(ProceduralVoxelGeneratorBehaviour p) => procedural = p;

		[SerializeField]
		internal Transform anchor;

		void Awake()
		{
			this.CreateVoxelMeshGridEntity(
				gameObject.GetInstanceID(),
				(transformAttachment || rolling) ? transform : null
			);

			if (!anchor)
				anchor = transform;

			if (rolling)
			{
				ConfigureRollingGrid();
			}
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

		void Update()
		{
			if (!rolling)
				return;
			// When driven by an external driver (e.g. RollingGridPlayerDriver), don't emit our own move requests
			if (externalDriver)
				return;
			if (!VoxelEntityBridge.TryGetEntityManager(out var em))
				return;
			var gid = gameObject.GetInstanceID();
			var origin = anchor ? anchor.position : transform.position;
			var chunkWorld = new Unity.Mathematics.int3(
				(int)math.floor(origin.x / (voxelSize * VoxelConstants.EFFECTIVE_CHUNK_SIZE)),
				(int)math.floor(origin.y / (voxelSize * VoxelConstants.EFFECTIVE_CHUNK_SIZE)),
				(int)math.floor(origin.z / (voxelSize * VoxelConstants.EFFECTIVE_CHUNK_SIZE))
			);
			VoxelEntityBridge.SendRollingMovementRequest(gid, chunkWorld);
		}

		void ConfigureRollingGrid()
		{
			if (!VoxelEntityBridge.TryGetEntityManager(out var em))
				return;
			var gid = gameObject.GetInstanceID();
			VoxelEntityBridge.EnableRollingForGrid(gid);
		}
	}
}
