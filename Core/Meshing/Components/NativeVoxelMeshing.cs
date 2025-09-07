namespace Voxels.Core.Meshing.Components
{
	using ThirdParty.SurfaceNets;
	using ThirdParty.SurfaceNets.Utils;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Jobs;
	using Unity.Mathematics;
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

		public FairingBuffers fairing;

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
			fairing = new FairingBuffers(allocator);
		}

		public void Dispose()
		{
			vertices.Dispose();
			indices.Dispose();
			bounds.Dispose();
			buffer.Dispose();
			fairing.Dispose();
		}

		public JobHandle Dispose(JobHandle inputDeps)
		{
			inputDeps = vertices.Dispose(inputDeps);
			inputDeps = indices.Dispose(inputDeps);
			inputDeps = bounds.Dispose(inputDeps);
			inputDeps = buffer.Dispose(inputDeps);
			inputDeps = fairing.Dispose(inputDeps);
			return inputDeps;
		}
	}

	/// <summary>
	///   Pre-allocated buffers for surface fairing pipeline.
	///   Uses flexible NativeList for dynamic vertex counts.
	/// </summary>
	public struct FairingBuffers : INativeDisposable
	{
		public NativeList<int3> cellCoords;
		public NativeList<int> cellLinearIndex;
		public NativeList<byte> materialIds;
		public NativeList<float4> materialWeights;
		public NativeArray<int> cellToVertex; // Dense array sized CHUNK_SIZE^3
		public NativeList<int2> neighborIndexRanges;
		public NativeList<int> neighborIndices;
		public NativeList<float3> positionsA;
		public NativeList<float3> positionsB;
		public NativeList<float3> normals;

		public FairingBuffers(Allocator allocator)
		{
			// Conservative initial capacity for typical chunks
			const int initialVertexCapacity = 1024;
			const int totalCells = CHUNK_SIZE * CHUNK_SIZE * CHUNK_SIZE;

			cellCoords = new NativeList<int3>(initialVertexCapacity, allocator);
			cellLinearIndex = new NativeList<int>(initialVertexCapacity, allocator);
			materialIds = new NativeList<byte>(initialVertexCapacity, allocator);
			materialWeights = new NativeList<float4>(initialVertexCapacity, allocator);
			cellToVertex = new NativeArray<int>(totalCells, allocator); // Keep as fixed-size dense array
			neighborIndexRanges = new NativeList<int2>(initialVertexCapacity, allocator);
			neighborIndices = new NativeList<int>(initialVertexCapacity * 6, allocator); // Max 6 face neighbors
			positionsA = new NativeList<float3>(initialVertexCapacity, allocator);
			positionsB = new NativeList<float3>(initialVertexCapacity, allocator);
			normals = new NativeList<float3>(initialVertexCapacity, allocator);
		}

		public void Dispose()
		{
			cellCoords.Dispose();
			cellLinearIndex.Dispose();
			materialIds.Dispose();
			materialWeights.Dispose();
			if (cellToVertex.IsCreated)
				cellToVertex.Dispose();
			neighborIndexRanges.Dispose();
			neighborIndices.Dispose();
			positionsA.Dispose();
			positionsB.Dispose();
			normals.Dispose();
		}

		public JobHandle Dispose(JobHandle inputDeps)
		{
			inputDeps = cellCoords.Dispose(inputDeps);
			inputDeps = cellLinearIndex.Dispose(inputDeps);
			inputDeps = materialIds.Dispose(inputDeps);
			inputDeps = materialWeights.Dispose(inputDeps);
			if (cellToVertex.IsCreated)
				inputDeps = cellToVertex.Dispose(inputDeps);
			inputDeps = neighborIndexRanges.Dispose(inputDeps);
			inputDeps = neighborIndices.Dispose(inputDeps);
			inputDeps = positionsA.Dispose(inputDeps);
			inputDeps = positionsB.Dispose(inputDeps);
			inputDeps = normals.Dispose(inputDeps);
			return inputDeps;
		}
	}
}
