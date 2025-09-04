namespace Voxels.Core.Meshing
{
	using ThirdParty.SurfaceNets.Extensions;
	using UnityEngine;
	using static Diagnostics.VoxelProfiler.Marks;
	using static UnityEngine.Rendering.MeshUpdateFlags;

	public static class ManagedVoxelMeshingApply
	{
		public static void ApplyMeshManaged(ref this NativeVoxelMesh nvm)
		{
			using var _ = NativeVoxelMeshingProcessor_Apply.Auto();
			if (!nvm.meshing.meshRef)
				nvm.meshing.meshRef = new Mesh();

			Mesh.ApplyAndDisposeWritableMeshData(
				nvm.meshing.meshData,
				nvm.meshing.meshRef,
				DontValidateIndices | DontResetBoneBounds | DontRecalculateBounds
#if UNITY_6000_2_OR_NEWER
					| DontValidateLodRanges
#endif
			);
			nvm.meshing.meshData = default;

			nvm.meshing.meshRef.Value.bounds = nvm.meshing.bounds.Item.ToBounds();
		}
	}
}
