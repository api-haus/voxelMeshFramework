namespace Voxels.Core.Meshing
{
	using ThirdParty.SurfaceNets;
	using ThirdParty.SurfaceNets.Extensions;
	using ThirdParty.SurfaceNets.Utils;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Jobs;
	using Unity.Mathematics.Geometry;
	using UnityEngine;
	using UnityEngine.Rendering;
	using static SharedStaticMeshingResources;
	using static UnityEngine.Rendering.MeshUpdateFlags;

	[BurstCompile]
	public struct UploadMeshJob : IJob
	{
		[ReadOnly]
		public NativeList<int> indices;

		[ReadOnly]
		public UnsafePointer<MinMaxAABB> bounds;

		[ReadOnly]
		public NativeList<Vertex> vertices;

		public Mesh.MeshDataArray mda;

		public void Execute()
		{
			var md = mda[0];

			md.SetVertexBufferParams(vertices.Length, VertexAttributes);
			md.SetIndexBufferParams(indices.Length, IndexFormat.UInt32);

			md.subMeshCount = 1;
			md.SetSubMesh(
				0,
				new SubMeshDescriptor(0, indices.Length) { bounds = bounds.Item.ToBounds() },
				DontValidateIndices | DontResetBoneBounds | DontRecalculateBounds | DontValidateLodRanges
			);

			md.GetVertexData<Vertex>().CopyFrom(vertices.AsArray());
			md.GetIndexData<int>().CopyFrom(indices.AsArray());
		}
	}
}
