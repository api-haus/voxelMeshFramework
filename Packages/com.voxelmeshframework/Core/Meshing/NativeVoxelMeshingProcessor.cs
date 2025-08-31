namespace Voxels.Core.Meshing
{
	using ThirdParty.SurfaceNets;
	using ThirdParty.SurfaceNets.Extensions;
	using Unity.Jobs;
	using UnityEngine;
	using static Diagnostics.VoxelProfiler.Marks;
	using static UnityEngine.Rendering.MeshUpdateFlags;

	public static class NativeVoxelMeshingProcessor
	{
		public static JobHandle ScheduleMeshing(ref this NativeVoxelMesh nvm, JobHandle inputDeps)
		{
			using var _ = NativeVoxelMeshingProcessor_Schedule.Auto();
			// Use NaiveSurfaceNets directly for now
			inputDeps = new NaiveSurfaceNets
			{
				edgeTable = SharedStaticMeshingResources.EdgeTable,
				volume = nvm.volume.sdfVolume,
				buffer = nvm.meshing.buffer,
				indices = nvm.meshing.indices,
				vertices = nvm.meshing.vertices,
				bounds = nvm.meshing.bounds,
				recalculateNormals = true,
				voxelSize = nvm.volume.voxelSize,
			}.Schedule(inputDeps);

			inputDeps = new UploadMeshJob
			{
				mda = nvm.meshing.meshData,
				bounds = nvm.meshing.bounds,
				indices = nvm.meshing.indices,
				vertices = nvm.meshing.vertices,
			}.Schedule(inputDeps);

			return inputDeps;
		}

		public static void ApplyMeshManaged(ref this NativeVoxelMesh nvm)
		{
			using var _ = NativeVoxelMeshingProcessor_Apply.Auto();
			if (!nvm.meshing.meshRef)
				nvm.meshing.meshRef = new Mesh();

			Mesh.ApplyAndDisposeWritableMeshData(
				nvm.meshing.meshData,
				nvm.meshing.meshRef,
				DontValidateIndices | DontResetBoneBounds | DontRecalculateBounds | DontValidateLodRanges
			);
			nvm.meshing.meshData = default;

			nvm.meshing.meshRef.Value.bounds = nvm.meshing.bounds.Item.ToBounds();
		}
	}
}
