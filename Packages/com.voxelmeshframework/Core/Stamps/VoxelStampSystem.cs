namespace Voxels.Core.Stamps
{
	using Atlasing.Components;
	using Authoring;
	using Debugging;
	using Meshing.Components;
	using Meshing.Tags;
	using Spatial;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Jobs;
	using Unity.Mathematics;
	using UnityEngine;
	using static Concurrency.VoxelJobFenceRegistry;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Entities.SystemAPI;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;
	using VoxelMeshingSystem = Meshing.Scheduling.VoxelMeshingSystem;
	using VoxelSpatialSystem = Spatial.VoxelSpatialSystem;

	// [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
	[UpdateBefore(typeof(VoxelMeshingSystem))]
	public partial struct VoxelStampSystem : ISystem
	{
		EntityQuery m_StampQuery;

		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			state.RequireForUpdate<VoxelSpatialSystem.VoxelObjectHash>();
			state.RequireForUpdate<EndSimST>();

			m_StampQuery = QueryBuilder().WithAll<NativeVoxelStampProcedural>().Build();
			state.RequireForUpdate(m_StampQuery);
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			using var _ = VoxelStampSystem_Update.Auto();
			var ecb = GetSingleton<EndSimST>().CreateCommandBuffer(state.WorldUnmanaged);
			var sh = GetSingleton<VoxelSpatialSystem.VoxelObjectHash>();

			var gridIdToDims = default(NativeParallelHashMap<int, int3>);
			ProcessAllStamps(ref ecb, ref sh, ref gridIdToDims, ref state);

			JobHandle.ScheduleBatchedJobs();
			ecb.DestroyEntity(m_StampQuery, EntityQueryCaptureMode.AtPlayback);
		}

		/// <summary>
		///   Process all stamp entities and apply them to affected chunks
		/// </summary>
		void ProcessAllStamps(
			ref EntityCommandBuffer ecb,
			ref VoxelSpatialSystem.VoxelObjectHash sh,
			ref NativeParallelHashMap<int, int3> gridIdToDims,
			ref SystemState state
		)
		{
			using var stamps = m_StampQuery.ToComponentDataArray<NativeVoxelStampProcedural>(
				Allocator.TempJob
			);

			foreach (var stamp in stamps)
			{
				DrawStampDebugGizmos(stamp);
				using var objectsInBounds = sh.Query(stamp.bounds);

				ProcessStampOnChunks(stamp, objectsInBounds, ref gridIdToDims, ref ecb, ref state);
				SynchronizeAdjacentChunkOverlaps(objectsInBounds, ref ecb, ref state);
			}
		}

		/// <summary>
		///   Draw debug gizmos for stamp visualization
		/// </summary>
		static void DrawStampDebugGizmos(NativeVoxelStampProcedural stamp)
		{
#if ALINE && DEBUG
			if (VoxelDebugging.IsEnabled && VoxelDebugging.Flags.stampGizmos)
			{
				Visual.Draw.PushDuration(.33f);
				Visual.Draw.WireSphere(stamp.shape.sphere.center, stamp.shape.sphere.radius, Color.red);
				Visual.Draw.WireBox(stamp.bounds.Center, stamp.bounds.Extents, Color.red);
				Visual.Draw.PopDuration();
			}
#endif
		}

		/// <summary>
		///   Apply stamp to all chunks within bounds
		/// </summary>
		void ProcessStampOnChunks(
			NativeVoxelStampProcedural stamp,
			NativeList<SpatialVoxelObject> chunksInBounds,
			ref NativeParallelHashMap<int, int3> gridIdToDims,
			ref EntityCommandBuffer ecb,
			ref SystemState state
		)
		{
			foreach (var spatialVoxelObject in chunksInBounds)
				ApplyStampToChunk(stamp, spatialVoxelObject, ref ecb, ref state);
		}

		/// <summary>
		///   Apply stamp to a single chunk
		/// </summary>
		void ApplyStampToChunk(
			NativeVoxelStampProcedural stamp,
			SpatialVoxelObject spatialVoxelObject,
			ref EntityCommandBuffer ecb,
			ref SystemState state
		)
		{
			var nvmRw = GetComponentRW<NativeVoxelMesh>(spatialVoxelObject.entity);
			ref var nvm = ref nvmRw.ValueRW;

			using (VoxelStampSystem_Schedule.Auto())
			{
				var applyParams = new StampScheduling.StampApplyParams
				{
					sdfScale = 16f / spatialVoxelObject.voxelSize,
					deltaTime = SystemAPI.Time.DeltaTime,
					alphaPerSecond = 60f,
				};

				var applyStampJob = StampScheduling.ScheduleApplyStamp(
					stamp,
					spatialVoxelObject,
					nvm,
					applyParams,
					GetFence(spatialVoxelObject.entity)
				);

				UpdateFence(spatialVoxelObject.entity, applyStampJob);
			}

			DrawChunkDebugGizmos(spatialVoxelObject);
			ecb.SetComponentEnabled<NeedsRemesh>(spatialVoxelObject.entity, true);
		}

		/// <summary>
		///   Draw debug gizmos for chunk visualization
		/// </summary>
		static void DrawChunkDebugGizmos(SpatialVoxelObject spatialVoxelObject)
		{
#if ALINE && DEBUG
			if (VoxelDebugging.IsEnabled && VoxelDebugging.Flags.stampGizmos)
			{
				Visual.Draw.PushDuration(.12f);
				Visual.Draw.PushMatrix(spatialVoxelObject.ltw);
				Visual.Draw.WireBox(
					spatialVoxelObject.localBounds.Center,
					spatialVoxelObject.localBounds.Extents,
					Color.white
				);
				Visual.Draw.PopMatrix();
				Visual.Draw.PopDuration();
			}
#endif
		}

		/// <summary>
		///   Synchronize shared overlaps between adjacent chunks that were stamped
		/// </summary>
		void SynchronizeAdjacentChunkOverlaps(
			NativeList<SpatialVoxelObject> chunksInBounds,
			ref EntityCommandBuffer ecb,
			ref SystemState state
		)
		{
			for (var i = 0; i < chunksInBounds.Length; i++)
			for (var j = i + 1; j < chunksInBounds.Length; j++)
			{
				var a = chunksInBounds[i];
				var b = chunksInBounds[j];

				if (!HasComponent<AtlasedChunk>(a.entity) || !HasComponent<AtlasedChunk>(b.entity))
					continue;

				var ca = GetComponent<AtlasedChunk>(a.entity).coord;
				var cb = GetComponent<AtlasedChunk>(b.entity).coord;

				if (!StampScheduling.TryResolveAdjacency(ca, cb, out var axis, out var aIsSource))
					continue;

				SynchronizeChunkPair(a.entity, b.entity, axis, aIsSource, ref ecb, ref state);
			}
		}

		/// <summary>
		///   Synchronize shared overlap between a pair of adjacent chunks
		/// </summary>
		void SynchronizeChunkPair(
			Entity entityA,
			Entity entityB,
			int axis,
			bool aIsSource,
			ref EntityCommandBuffer ecb,
			ref SystemState state
		)
		{
			var src = aIsSource ? entityA : entityB;
			var dst = aIsSource ? entityB : entityA;

			var dep = JobHandle.CombineDependencies(GetFence(src), GetFence(dst));
			var srcNvm = GetComponentRW<NativeVoxelMesh>(src).ValueRO;
			var dstNvm = GetComponentRW<NativeVoxelMesh>(dst).ValueRO;
			var copyJob = StampScheduling.ScheduleCopySharedOverlap(srcNvm, dstNvm, (byte)axis, dep);

			// Update both src and dst fences to serialize subsequent copy jobs that
			// read from or write to either chunk in this frame.
			UpdateFence(src, copyJob);
			UpdateFence(dst, copyJob);

			ecb.SetComponentEnabled<NeedsRemesh>(dst, true);
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state) { }
	}
}
