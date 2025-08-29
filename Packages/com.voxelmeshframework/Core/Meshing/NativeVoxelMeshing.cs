namespace Voxels.Core.Meshing
{
	using ThirdParty.SurfaceNets;
	using ThirdParty.SurfaceNets.Utils;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Jobs;
	using Unity.Mathematics.Geometry;
	using UnityEngine;
	using static VoxelConstants;

	public struct NativeVoxelMeshing : INativeDisposable
	{
		public NativeList<Vertex> vertices;
		public NativeList<int> indices;
		public UnsafePointer<MinMaxAABB> bounds;
		public NativeArray<int> buffer;

		public UnityObjectRef<Mesh> meshRef;
		public Mesh.MeshDataArray meshData;

		public NativeVoxelMeshing(Allocator allocator = Allocator.Persistent)
		{
			const int initialVertexCapacity = 1024;
			const int bufferCapacity = (CHUNK_SIZE + 1) * (CHUNK_SIZE + 1) * 2;

			vertices = new(initialVertexCapacity, allocator);
			indices = new(initialVertexCapacity, allocator);
			bounds = new(default, allocator);
			buffer = new(bufferCapacity, allocator);

			meshRef = default;
			meshData = default;
		}

		public void Dispose()
		{
			vertices.Dispose();
			indices.Dispose();
			bounds.Dispose();
			buffer.Dispose();
		}

		public JobHandle Dispose(JobHandle inputDeps)
		{
			inputDeps = vertices.Dispose(inputDeps);
			inputDeps = indices.Dispose(inputDeps);
			inputDeps = bounds.Dispose(inputDeps);
			inputDeps = buffer.Dispose(inputDeps);
			return inputDeps;
		}
	}
}
