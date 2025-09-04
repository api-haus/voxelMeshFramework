namespace Voxels.Core.Meshing.Systems
{
	using System;
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

				ecb.SetComponentEnabled<NeedsRemesh>(entity, false);
				ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(entity, true);
			}

			JobHandle.ScheduleBatchedJobs();
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state) { }
	}
}
