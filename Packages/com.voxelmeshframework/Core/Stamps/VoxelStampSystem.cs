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
	using static Unity.Entities.SystemAPI;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;
	using VoxelSpatialSystem = Spatial.VoxelSpatialSystem;
#if ALINE && DEBUG
	using Debugging;
#endif

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
			var ecb = GetSingleton<EndSimST>().CreateCommandBuffer(state.WorldUnmanaged);
			var sh = GetSingleton<VoxelSpatialSystem.VoxelObjectHash>();

			using var stamps = m_StampQuery.ToComponentDataArray<NativeVoxelStampProcedural>(
				Allocator.TempJob
			);
			var concurrentStampJobs = state.Dependency;

			foreach (var stamp in stamps)
			{
				using var chunksInBounds = sh.Query(stamp.bounds);

				foreach (var spatialVoxelObject in chunksInBounds)
				{
					var sdf = spatialVoxelObject.voxelData.GetSDF();
					var mat = spatialVoxelObject.voxelData.GetMat();

					var applyStampJob = new ApplyVoxelStampJob
					{
						//
						stamp = stamp,
						volumeSdf = sdf,
						volumeMaterials = mat,
						volumeBounds = spatialVoxelObject.bounds,
						voxelSize = spatialVoxelObject.voxelSize,
					}.Schedule(state.Dependency);

					concurrentStampJobs = JobHandle.CombineDependencies(concurrentStampJobs, applyStampJob);

#if ALINE && DEBUG
					if (VoxelDebugging.IsEnabled)
					{
						Visual.Draw.PushDuration(.33f);
						Visual.Draw.WireBox(
							spatialVoxelObject.bounds.Center,
							spatialVoxelObject.bounds.Extents,
							Color.white
						);
						Visual.Draw.WireSphere(
							stamp.shape.sphere.center,
							stamp.shape.sphere.radius,
							Color.black
						);
						Visual.Draw.WireBox(stamp.bounds.Center, stamp.bounds.Extents, Color.black);
						Visual.Draw.PopDuration();
					}
#endif

					ecb.SetComponentEnabled<NeedsRemesh>(spatialVoxelObject.entity, true);
				}
			}

			state.Dependency = concurrentStampJobs;
			// state.Dependency = stamps.Dispose(state.Dependency);

			ecb.DestroyEntity(m_StampQuery, EntityQueryCaptureMode.AtPlayback);
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state) { }
	}
}
