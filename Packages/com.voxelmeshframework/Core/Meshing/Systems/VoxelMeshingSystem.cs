namespace Voxels.Core.Meshing.Systems
{
	using System;
	using Concurrency;
	using Tags;
	using Unity.Burst;
	using Unity.Entities;
	using Unity.Jobs;
	using Unity.Logging;
	using UnityEngine;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Entities.SystemAPI;
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
				if (!VoxelJobFenceRegistry.TryComplete(entity))
					continue;
#endif

				ref var nvm = ref nativeVoxelMeshRef.ValueRW;

				if (nvm.meshing.meshData.Length != 0)
					Log.Warning("mesh data is non zero when remeshing");

				nvm.meshing.meshData = Mesh.AllocateWritableMeshData(1);

				// Prepare input/output data
				var input = new MeshingInputData
				{
					volume = nvm.volume.sdfVolume,
					materials = nvm.volume.materials,
					voxelSize = nvm.volume.voxelSize,
					edgeTable = SharedStaticMeshingResources.EdgeTable,
					chunkSize = VoxelConstants.CHUNK_SIZE,
					normalsMode =
						algorithm.enableFairing && algorithm.recomputeNormalsAfterFairing
							? NormalsMode.NONE
							: NormalsMode.TRIANGLE_GEOMETRY,
					materialDistributionMode = algorithm.materialDistributionMode,
				};

				var output = new MeshingOutputData
				{
					vertices = nvm.meshing.vertices,
					indices = nvm.meshing.indices,
					buffer = nvm.meshing.buffer,
					bounds = nvm.meshing.bounds,
				};

				// Schedule appropriate algorithm
				var pre = VoxelJobFenceRegistry.Get(entity);
				JobHandle meshingJob;
				switch (algorithm.algorithm)
				{
					case VoxelMeshingAlgorithm.NAIVE_SURFACE_NETS:
						if (algorithm.enableFairing)
							meshingJob = new NaiveSurfaceNetsFairingScheduler
							{
								cellMargin = algorithm.cellMargin,
								fairingBuffers = nvm.meshing.fairing,
								fairingStepSize = algorithm.fairingStepSize,
								fairingIterations = algorithm.fairingIterations,
								recomputeNormalsAfterFairing = algorithm.recomputeNormalsAfterFairing,
							}.Schedule(input, output, pre);
						else
							meshingJob = new NaiveSurfaceNetsScheduler().Schedule(input, output, pre);
						break;
					case VoxelMeshingAlgorithm.DUAL_CONTOURING:
						meshingJob = pre;
						break;
					case VoxelMeshingAlgorithm.MARCHING_CUBES:
						meshingJob = pre;
						break;
					default:
						throw new NotImplementedException($"Algorithm {algorithm.algorithm} not implemented");
				}

				// Schedule mesh upload job
				meshingJob = new UploadMeshJob
				{
					mda = nvm.meshing.meshData,
					bounds = nvm.meshing.bounds,
					indices = nvm.meshing.indices,
					vertices = nvm.meshing.vertices,
				}.Schedule(meshingJob);

				VoxelJobFenceRegistry.Update(entity, meshingJob);

				ecb.SetComponentEnabled<NeedsRemesh>(entity, false);
				ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(entity, true);
			}

			JobHandle.ScheduleBatchedJobs();
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state) { }
	}
}
