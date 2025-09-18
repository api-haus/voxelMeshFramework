namespace Voxels.Core.Meshing.Scheduling
{
	using Algorithms;
	using Budgets;
	using Components;
	using Tags;
	using Unity.Burst;
	using Unity.Entities;
	using Unity.Jobs;
	using Unity.Logging;
	using UnityEngine;
	using static Concurrency.VoxelJobFenceRegistry;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Entities.SystemAPI;
	using static VoxelConstants;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;
	using Entity = Unity.Entities.Entity;
	using EntityCommandBuffer = Unity.Entities.EntityCommandBuffer;
	using ISystem = Unity.Entities.ISystem;
	using Random = Unity.Mathematics.Random;
	using SystemState = Unity.Entities.SystemState;

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

			ScheduleMeshingForEntities(ref state, ref ecb);

			JobHandle.ScheduleBatchedJobs();
		}

		/// <summary>
		///   Schedule meshing for all entities requiring remesh
		/// </summary>
		void ScheduleMeshingForEntities(ref SystemState state, ref EntityCommandBuffer ecb)
		{
			var toProcess = MeshingBudgets.Current.perFrame.meshingSchedule;

			foreach (
				var (nativeVoxelMeshRef, algorithm, entity) in Query<
					RefRW<NativeVoxelMesh>,
					VoxelMeshingAlgorithmComponent
				>()
					.WithAll<NeedsRemesh>()
					.WithNone<NeedsManagedMeshUpdate>()
					.WithEntityAccess()
			)
			{
				ref var nvm = ref nativeVoxelMeshRef.ValueRW;
				ScheduleMeshingForEntity(ref state, ref nvm, algorithm, entity, ref ecb);

				if (--toProcess <= 0)
					return;
			}
		}

		/// <summary>
		///   Schedule meshing for a single entity
		/// </summary>
		void ScheduleMeshingForEntity(
			ref SystemState state,
			ref NativeVoxelMesh nvm,
			VoxelMeshingAlgorithmComponent algorithm,
			Entity entity,
			ref EntityCommandBuffer ecb
		)
		{
			// Allocate mesh data
			if (nvm.meshing.meshData.Length != 0)
			{
				Log.Warning("scheduling and mesh data is not zero");
				return;
			}

			nvm.meshing.meshData = Mesh.AllocateWritableMeshData(1);

			var input = new MeshingInputData
			{
				volume = nvm.volume.sdfVolume,
				materials = nvm.volume.materials,
				voxelSize = nvm.volume.voxelSize,
				edgeTable = SharedStaticMeshingResources.EdgeTable,
				chunkSize = CHUNK_SIZE,
				normalsMode = algorithm.normalsMode,
				materialEncoding = algorithm.materialEncoding,
				positionJitter = Random.CreateFromIndex((uint)entity.Index).NextFloat(0f, 0.0005f),
			};

			var output = new MeshingOutputData
			{
				vertices = nvm.meshing.vertices,
				indices = nvm.meshing.indices,
				buffer = nvm.meshing.buffer,
				bounds = nvm.meshing.bounds,
			};

			var pre = GetFence(entity);
			var meshingJob = MeshingScheduling.ScheduleAlgorithm(input, output, algorithm, pre);

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

		[BurstCompile]
		public void OnDestroy(ref SystemState state) { }
	}
}
