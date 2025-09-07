namespace Voxels.Core.Meshing.Utilities
{
	using System;
	using System.Collections.Generic;
	using Algorithms;
	using Components;
	using Managed;
	using Procedural.Generators;
	using Stamps;
	using Unity.Collections;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using UnityEngine;
	using static VoxelConstants;

	public static class GridMeshingBuilder
	{
		public static Mesh BuildCombinedMesh(
			in BuildOptions options,
			in IProceduralVoxelGenerator generator,
			Func<NativeVoxelMesh, IMeshingAlgorithmScheduler> schedulerFactory
		)
		{
			if (options.gridDims.x <= 0 || options.gridDims.y <= 0 || options.gridDims.z <= 0)
				throw new ArgumentException("gridDims must be positive in all axes");

			var edgeTable = SharedStaticMeshingResources.EdgeTable;
			var voxelSize = options.voxelSize;
			var dims = options.gridDims;

			var chunks = new NativeList<NativeVoxelMesh>(dims.x * dims.y * dims.z, Allocator.Temp);
			var chunkCoords = new NativeList<int3>(dims.x * dims.y * dims.z, Allocator.Temp);

			try
			{
				for (var x = 0; x < dims.x; x++)
				for (var y = 0; y < dims.y; y++)
				for (var z = 0; z < dims.z; z++)
				{
					var nvm = new NativeVoxelMesh(Allocator.TempJob);
					nvm.volume.voxelSize = voxelSize;

					var coord = new int3(x, y, z);
					var origin = (float3)coord * (EFFECTIVE_CHUNK_SIZE * voxelSize);
					var bounds = MinMaxAABB.CreateFromCenterAndExtents(
						origin + (new float3(CHUNK_SIZE * 0.5f) * voxelSize),
						new float3(CHUNK_SIZE * voxelSize)
					);

					var ltw = float4x4.identity;
					var handle = generator.Schedule(bounds, ltw, voxelSize, nvm.volume, default);
					handle.Complete();

					chunks.Add(nvm);
					chunkCoords.Add(coord);
				}

				for (var i = 0; i < chunks.Length; i++)
				for (var j = i + 1; j < chunks.Length; j++)
				{
					var ca = chunkCoords[i];
					var cb = chunkCoords[j];
					if (!StampScheduling.TryResolveAdjacency(ca, cb, out var axis, out var aIsSource))
						continue;
					var src = aIsSource ? chunks[i] : chunks[j];
					var dst = aIsSource ? chunks[j] : chunks[i];
					StampScheduling.ScheduleCopySharedOverlap(src, dst, axis).Complete();
				}

				var combines = new List<CombineInstance>(chunks.Length);
				for (var i = 0; i < chunks.Length; i++)
				{
					var nvm = chunks[i];
					var input = new MeshingInputData
					{
						volume = nvm.volume.sdfVolume,
						materials = nvm.volume.materials,
						edgeTable = edgeTable,
						voxelSize = voxelSize,
						chunkSize = CHUNK_SIZE,
						normalsMode = options.algorithm.normalsMode,
						materialEncoding = options.algorithm.materialEncoding,
					};

					var output = new MeshingOutputData
					{
						vertices = nvm.meshing.vertices,
						indices = nvm.meshing.indices,
						buffer = nvm.meshing.buffer,
						bounds = nvm.meshing.bounds,
					};

					var sched = schedulerFactory(nvm);
					sched.Schedule(input, output, default).Complete();

					// Upload
					nvm.meshing.meshData = Mesh.AllocateWritableMeshData(1);
					new UploadMeshJob
					{
						indices = nvm.meshing.indices,
						bounds = nvm.meshing.bounds,
						vertices = nvm.meshing.vertices,
						mda = nvm.meshing.meshData,
					}
						.Schedule()
						.Complete();
					nvm.ApplyMeshManaged();

					var mesh = nvm.meshing.meshRef.Value;
					var subIndexCount = (int)mesh.GetIndexCount(0);
					if (mesh.vertexCount <= 0 || subIndexCount <= 0)
						continue;

					var origin = (float3)chunkCoords[i] * (EFFECTIVE_CHUNK_SIZE * voxelSize);
					combines.Add(
						new CombineInstance
						{
							mesh = mesh,
							subMeshIndex = 0,
							transform = Matrix4x4.TRS(origin, Quaternion.identity, Vector3.one),
						}
					);
				}

				var combined = ManagedMeshCombine.CombineIntoNewMesh(combines.ToArray());
				combined.name = $"Grid_{dims.x}x{dims.y}x{dims.z}";
				combined.RecalculateBounds();
				return combined;
			}
			finally
			{
				for (var i = 0; i < chunks.Length; i++)
					chunks[i].Dispose();
				chunks.Dispose();
				chunkCoords.Dispose();
			}
		}

		public struct BuildOptions
		{
			public int3 gridDims; // e.g., (2,2,2)
			public float voxelSize; // world units per voxel
			public VoxelMeshingAlgorithmComponent algorithm; // algorithm parameters
		}
	}
}
