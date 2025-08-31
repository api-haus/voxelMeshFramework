namespace Voxels.Core.Meshing.Systems
{
	using System;
	using Fairing;
	using Tags;
	using Unity.Burst;
	using Unity.Entities;
	using Unity.Jobs;
	using UnityEngine;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Entities.SystemAPI;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;

	[RequireMatchingQueriesForUpdate]
	public partial struct VoxelMeshingSystem : ISystem
	{
		// EntityQuery m_NeedsRemeshQuery;

		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			state.RequireForUpdate<EndSimST>();

			// m_NeedsRemeshQuery = QueryBuilder().WithAll<NativeVoxelMesh, NeedsRemesh>().Build();
			// state.RequireForUpdate(m_NeedsRemeshQuery);
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			using var _ = VoxelMeshingSystem_Update.Auto();

			var beginProfileJob = state.Dependency;
			var concurrentJobs = beginProfileJob;

			var ecb = GetSingleton<EndSimST>().CreateCommandBuffer(state.WorldUnmanaged);

			// enqueue
			foreach (
				var (nativeVoxelMeshRef, algorithm, entity) in Query<
					RefRW<NativeVoxelMesh>,
					VoxelMeshingAlgorithmComponent
				>()
					.WithAll<NeedsRemesh>()
					.WithEntityAccess()
			)
			{
				ref var nvm = ref nativeVoxelMeshRef.ValueRW;

				if (nvm.meshing.meshData.Length == 0)
					nvm.meshing.meshData = Mesh.AllocateWritableMeshData(1);

				// Prepare input/output data
				var input = new MeshingInputData
				{
					volume = nvm.volume.sdfVolume,
					materials = nvm.volume.materials,
					edgeTable = SharedStaticMeshingResources.EdgeTable,
					voxelSize = nvm.volume.voxelSize,
					chunkSize = VoxelConstants.CHUNK_SIZE,
					recalculateNormals = true,
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
				var meshingJob = algorithm.algorithm switch
				{
					VoxelMeshingAlgorithm.NAIVE_SURFACE_NETS => ScheduleNaiveSurfaceNetsWithOptionalFairing(
						algorithm,
						input,
						output,
						nvm.meshing.fairing,
						beginProfileJob
					),
					VoxelMeshingAlgorithm.DUAL_CONTOURING => beginProfileJob,
					VoxelMeshingAlgorithm.MARCHING_CUBES => beginProfileJob,
					_ => throw new NotImplementedException(
						$"Algorithm {algorithm.algorithm} not implemented"
					),
				};

				// Schedule mesh upload job
				meshingJob = new UploadMeshJob
				{
					mda = nvm.meshing.meshData,
					bounds = nvm.meshing.bounds,
					indices = nvm.meshing.indices,
					vertices = nvm.meshing.vertices,
				}.Schedule(meshingJob);

				concurrentJobs = JobHandle.CombineDependencies(meshingJob, concurrentJobs);

				ecb.SetComponentEnabled<NeedsRemesh>(entity, false);
				ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(entity, true);
			}

			state.Dependency = concurrentJobs;
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state) { }

		/// <summary>
		///   Schedules NaiveSurfaceNets with optional surface fairing post-processing.
		///   Uses pre-allocated buffers and maintains complete async job chain.
		/// </summary>
		JobHandle ScheduleNaiveSurfaceNetsWithOptionalFairing(
			VoxelMeshingAlgorithmComponent algorithm,
			in MeshingInputData input,
			in MeshingOutputData output,
			FairingBuffers fairingBuffers,
			JobHandle inputDeps
		)
		{
			// ===== SCHEDULE BASE NAIVE SURFACE NETS =====
			var meshingJob = new NaiveSurfaceNetsScheduler().Schedule(input, output, inputDeps);

			// ===== EARLY EXIT IF FAIRING DISABLED =====
			if (!algorithm.enableFairing)
				return meshingJob;

			// ===== USE PRE-ALLOCATED FAIRING BUFFERS =====
			// Use pre-allocated buffers and maintain async job chain

			// ===== FAIRING PIPELINE: ASYNC JOB CHAIN =====
			var job1 = new ExtractVertexDataJob
			{
				vertices = output.vertices,
				outPositions = fairingBuffers.positionsA,
				outMaterialIds = fairingBuffers.materialIds,
			}.Schedule(meshingJob);

			var job2 = new DeriveCellCoordsJob
			{
				vertices = output.vertices,
				positions = fairingBuffers.positionsA,
				voxelSize = input.voxelSize,
				cellCoords = fairingBuffers.cellCoords,
				cellLinearIndex = fairingBuffers.cellLinearIndex,
			}.Schedule(job1);

			var job3 = new BuildCellToVertexMapJob
			{
				vertices = output.vertices,
				cellLinearIndex = fairingBuffers.cellLinearIndex,
				cellToVertex = fairingBuffers.cellToVertex,
			}.Schedule(job2);

			var job4 = new BuildNeighborsJob
			{
				vertices = output.vertices,
				cellCoords = fairingBuffers.cellCoords,
				cellToVertex = fairingBuffers.cellToVertex,
				neighborIndexRanges = fairingBuffers.neighborIndexRanges,
				neighborIndices = fairingBuffers.neighborIndices,
			}.Schedule(job3);

			// ===== FAIRING ITERATIONS WITH PING-PONG =====
			var currentJobHandle = job4;
			var usePositionsB = false;

			for (var iteration = 0; iteration < algorithm.fairingIterations; iteration++)
			{
				var inBuffer = usePositionsB ? fairingBuffers.positionsB : fairingBuffers.positionsA;
				var outBuffer = usePositionsB ? fairingBuffers.positionsA : fairingBuffers.positionsB;

				currentJobHandle = new SurfaceFairingJob
				{
					vertices = output.vertices,
					inPositions = inBuffer,
					neighborIndexRanges = fairingBuffers.neighborIndexRanges,
					neighborIndices = fairingBuffers.neighborIndices,
					materialId = fairingBuffers.materialIds,
					cellCoords = fairingBuffers.cellCoords,
					outPositions = outBuffer,
					voxelSize = input.voxelSize,
					cellMargin = algorithm.cellMargin,
					fairingStepSize = algorithm.fairingStepSize,
				}.Schedule(currentJobHandle);

				usePositionsB = !usePositionsB; // Toggle buffers
			}

			// ===== UPDATE VERTICES WITH FINAL POSITIONS =====
			var finalPositions = usePositionsB ? fairingBuffers.positionsB : fairingBuffers.positionsA;
			var updateVerticesJob = new UpdateVertexPositionsJob
			{
				vertices = output.vertices,
				newPositions = finalPositions,
			}.Schedule(currentJobHandle);

			// ===== OPTIONAL NORMALS RECALCULATION =====
			var finalJob = updateVerticesJob;
			if (algorithm.recomputeNormalsAfterFairing)
			{
				var clearNormalsJob = new ClearNormalsJob
				{
					vertices = output.vertices,
					normals = fairingBuffers.normals,
				}.Schedule(finalJob);

				var recalcNormalsJob = new RecalculateNormalsJob
				{
					indices = output.indices.AsDeferredJobArray(),
					vertices = output.vertices,
					normals = fairingBuffers.normals,
				}.Schedule(clearNormalsJob);

				var normalizeJob = new NormalizeNormalsJob
				{
					vertices = output.vertices,
					normals = fairingBuffers.normals,
				}.Schedule(recalcNormalsJob);

				finalJob = new UpdateVertexNormalsJob
				{
					vertices = output.vertices,
					newNormals = fairingBuffers.normals,
				}.Schedule(normalizeJob);
			}

			return finalJob;
		}
	}
}
