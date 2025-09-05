namespace Voxels.Core.Meshing.Systems
{
	using System;
	using Grids;
	using Tags;
	using Unity.Burst;
	using Unity.Entities;
	using Unity.Jobs;
	using UnityEngine;
	using static Concurrency.VoxelJobFenceRegistry;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Entities.SystemAPI;
	using static VoxelConstants;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;

	[RequireMatchingQueriesForUpdate]
	public partial struct VoxelMeshingSystem : ISystem
	{
		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			state.RequireForUpdate<EndSimST>();
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			using var _ = VoxelMeshingSystem_Update.Auto();

			var ecb = GetSingleton<EndSimST>().CreateCommandBuffer(state.WorldUnmanaged);

			// schedule meshing
			foreach (
				var (nativeVoxelMeshRef, algorithm, entity) in Query<
					RefRW<NativeVoxelMesh>,
					VoxelMeshingAlgorithmComponent
				>()
					.WithAll<NeedsRemesh>()
					.WithEntityAccess()
			)
			{
#if !VMF_TAIL_PIPELINE
				// Avoid scheduling reads while volume modifications are still in-flight
				if (!TryComplete(entity))
					continue;
#endif

				ref var nvm = ref nativeVoxelMeshRef.ValueRW;

				if (nvm.meshing.meshData.Length != 0)
					// Log.Warning("mesh data is non zero when remeshing");
					continue;

				nvm.meshing.meshData = Mesh.AllocateWritableMeshData(1);

				// Prepare input/output data
				var input = new MeshingInputData
				{
					volume = nvm.volume.sdfVolume,
					materials = nvm.volume.materials,
					voxelSize = nvm.volume.voxelSize,
					edgeTable = SharedStaticMeshingResources.EdgeTable,
					chunkSize = CHUNK_SIZE,
					normalsMode = algorithm.normalsMode,
					materialDistributionMode = algorithm.materialDistributionMode,
					copyApronPostMesh = true,
				};

				var output = new MeshingOutputData
				{
					vertices = nvm.meshing.vertices,
					indices = nvm.meshing.indices,
					buffer = nvm.meshing.buffer,
					bounds = nvm.meshing.bounds,
				};

				// Schedule appropriate algorithm via shared static
				var pre = GetFence(entity);
				var meshingJob = MeshingScheduling.ScheduleAlgorithm(
					input,
					output,
					algorithm,
					ref nvm.meshing.fairing,
					pre
				);

				// Schedule mesh upload job
				meshingJob = new UploadMeshJob
				{
					mda = nvm.meshing.meshData,
					bounds = nvm.meshing.bounds,
					indices = nvm.meshing.indices,
					vertices = nvm.meshing.vertices,
				}.Schedule(meshingJob);

				UpdateFence(entity, meshingJob);

				// mark first-time processed for per-grid progress
				if (!state.EntityManager.HasComponent<ProcessedOnce>(entity))
				{
					ecb.AddComponent<ProcessedOnce>(entity);
					ecb.SetComponentEnabled<ProcessedOnce>(entity, true);
					if (state.EntityManager.HasComponent<NativeVoxelChunk>(entity))
					{
						var gid = state.EntityManager.GetComponentData<NativeVoxelChunk>(entity).gridID;
						var gq = QueryBuilder().WithAll<NativeVoxelGrid>().Build();
						using var grids = gq.ToEntityArray(Unity.Collections.Allocator.Temp);
						using var gridDatas = gq.ToComponentDataArray<NativeVoxelGrid>(
							Unity.Collections.Allocator.Temp
						);
						for (var i = 0; i < grids.Length; i++)
						{
							if (gridDatas[i].gridID != gid)
								continue;
							var grid = grids[i];
							var prog = state.EntityManager.HasComponent<GridMeshingProgress>(grid)
								? state.EntityManager.GetComponentData<GridMeshingProgress>(grid)
								: default;
							prog.processedCount++;
							ecb.SetComponent(grid, prog);
							break;
						}
					}
				}

				ecb.SetComponentEnabled<NeedsRemesh>(entity, false);
				ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(entity, true);
			}

			JobHandle.ScheduleBatchedJobs();
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state) { }
	}
}
