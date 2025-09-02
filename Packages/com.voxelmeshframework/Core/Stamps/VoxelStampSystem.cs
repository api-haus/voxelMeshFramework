namespace Voxels.Core.Stamps
{
	using Authoring;
	using Meshing.Systems;
	using Meshing.Tags;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Jobs;
	using UnityEngine;
	using Voxels.Core.Concurrency;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Entities.SystemAPI;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;
	using VoxelSpatialSystem = Spatial.VoxelSpatialSystem;
#if ALINE && DEBUG
	using Debugging;
#endif

	[WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
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
				if (VoxelDebugging.IsEnabled)
				{
					Visual.Draw.PushDuration(.33f);
					Visual.Draw.WireSphere(stamp.shape.sphere.center, stamp.shape.sphere.radius, Color.black);
					Visual.Draw.WireBox(stamp.bounds.Center, stamp.bounds.Extents, Color.black);
					Visual.Draw.PopDuration();
				}
#endif

				using var chunksInBounds = sh.Query(stamp.bounds);

				foreach (var spatialVoxelObject in chunksInBounds)
				{
					// Use the entity's NativeVoxelMesh buffers directly to preserve safety handles
					var nvmRw = GetComponentRW<Meshing.NativeVoxelMesh>(spatialVoxelObject.entity);
					ref var nvm = ref nvmRw.ValueRW;
					var sdf = nvm.volume.sdfVolume;
					var mat = nvm.volume.materials;

					using (VoxelStampSystem_Schedule.Auto())
					{
						// Avoid scheduling writes while previous work for this entity is still in-flight
						if (!VoxelJobFenceRegistry.TryComplete(spatialVoxelObject.entity))
							continue;

						var pre = VoxelJobFenceRegistry.Get(spatialVoxelObject.entity);
						var applyStampJob = new ApplyVoxelStampJob
						{
							//
							stamp = stamp,
							volumeSdf = sdf,
							volumeMaterials = mat,
							localVolumeBounds = spatialVoxelObject.localBounds,
							volumeLTW = spatialVoxelObject.ltw,
							volumeWtl = spatialVoxelObject.wtl,
							voxelSize = spatialVoxelObject.voxelSize,
						}.Schedule(pre);

						VoxelJobFenceRegistry.Update(spatialVoxelObject.entity, applyStampJob);
					}

#if ALINE && DEBUG
					if (VoxelDebugging.IsEnabled)
					{
						Visual.Draw.PushDuration(.33f);
						Visual.Draw.WireBox(
							spatialVoxelObject.localBounds.Center,
							spatialVoxelObject.localBounds.Extents,
							Color.white
						);
						Visual.Draw.PopDuration();
					}
#endif

					ecb.SetComponentEnabled<NeedsRemesh>(spatialVoxelObject.entity, true);
				}
			}

			JobHandle.ScheduleBatchedJobs();
			ecb.DestroyEntity(m_StampQuery, EntityQueryCaptureMode.AtPlayback);
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state) { }
	}
}
