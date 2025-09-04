namespace Voxels.Core.Stamps
{
	using Authoring;
	using Debugging;
	using Grids;
	using Meshing;
	using Meshing.Systems;
	using Meshing.Tags;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Jobs;
	using UnityEngine;
	using static Concurrency.VoxelJobFenceRegistry;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Entities.SystemAPI;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;
	using VoxelSpatialSystem = Spatial.VoxelSpatialSystem;

	[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
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

			using var stamps = m_StampQuery.ToComponentDataArray<NativeVoxelStampProcedural>(
				Allocator.TempJob
			);

			foreach (var stamp in stamps)
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

				using var chunksInBounds = sh.Query(stamp.bounds);

				foreach (var spatialVoxelObject in chunksInBounds)
				{
					// Use the entity's NativeVoxelMesh buffers directly to preserve safety handles
					var nvmRw = GetComponentRW<NativeVoxelMesh>(spatialVoxelObject.entity);
					ref var nvm = ref nvmRw.ValueRW;

					using (VoxelStampSystem_Schedule.Auto())
					{
						// Avoid scheduling writes while previous work for this entity is still in-flight
#if !VMF_TAIL_PIPELINE
						if (!TryComplete(spatialVoxelObject.entity))
							continue;
#endif

						var applyParams = new StampScheduling.StampApplyParams
						{
							sdfScale = 16f / spatialVoxelObject.voxelSize,
							deltaTime = SystemAPI.Time.DeltaTime,
							alphaPerSecond = 20f,
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

#if ALINE && DEBUG
					if (VoxelDebugging.IsEnabled && VoxelDebugging.Flags.stampGizmos)
					{
						Visual.Draw.PushDuration(.33f);
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

					ecb.SetComponentEnabled<NeedsRemesh>(spatialVoxelObject.entity, true);
				}

				// Synchronize the 2-voxel shared overlap between adjacent chunks that were stamped
				for (var i = 0; i < chunksInBounds.Length; i++)
				for (var j = i + 1; j < chunksInBounds.Length; j++)
				{
					var a = chunksInBounds[i];
					var b = chunksInBounds[j];

					if (
						!HasComponent<NativeVoxelChunk>(a.entity) || !HasComponent<NativeVoxelChunk>(b.entity)
					)
						continue;

					var ca = GetComponent<NativeVoxelChunk>(a.entity).coord;
					var cb = GetComponent<NativeVoxelChunk>(b.entity).coord;

					if (!StampScheduling.TryResolveAdjacency(ca, cb, out var axis, out var aIsSource))
						continue;

					var src = aIsSource ? a.entity : b.entity;
					var dst = aIsSource ? b.entity : a.entity;

					var dep = JobHandle.CombineDependencies(GetFence(src), GetFence(dst));

					var srcNvm = GetComponentRW<NativeVoxelMesh>(src).ValueRO;
					var dstNvm = GetComponentRW<NativeVoxelMesh>(dst).ValueRO;

					var copyJob = StampScheduling.ScheduleCopySharedOverlap(srcNvm, dstNvm, axis, dep);

					// Update both src and dst fences to serialize subsequent copy jobs that
					// read from or write to either chunk in this frame.
					UpdateFence(src, copyJob);
					UpdateFence(dst, copyJob);
					ecb.SetComponentEnabled<NeedsRemesh>(dst, true);
				}
			}

			JobHandle.ScheduleBatchedJobs();
			ecb.DestroyEntity(m_StampQuery, EntityQueryCaptureMode.AtPlayback);
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state) { }
	}
}
