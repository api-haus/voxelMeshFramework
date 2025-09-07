namespace Voxels.Core.Spatial
{
	using System;
	using Atlasing.Components;
	using Authoring;
	using Debugging;
	using Meshing.Components;
	using Meshing.Tags;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using Unity.Transforms;
	using UnityEngine;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Entities.SystemAPI;
	using static Unity.Mathematics.Geometry.Math;
	using static Unity.Mathematics.math;
	using static VoxelConstants;
	using EndInitST = Unity.Entities.EndInitializationEntityCommandBufferSystem.Singleton;

	[UpdateInGroup(typeof(InitializationSystemGroup))]
	public partial struct VoxelSpatialSystem : ISystem
	{
		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			state.RequireForUpdate<EndInitST>();

			const int spatialObjectCapacity = 16384;

			state.EntityManager.CreateSingleton(
				new VoxelObjectHash
				{
					//
					hash = new(
						//
						spatialObjectCapacity,
						Allocator.Persistent
					),
				}
			);
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state)
		{
			GetSingleton<VoxelObjectHash>().Dispose();
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			using var _ = VoxelSpatialSystem_Update.Auto();
			var ecb = GetSingleton<EndInitST>().CreateCommandBuffer(state.WorldUnmanaged);

			ref var st = ref GetSingletonRW<VoxelObjectHash>().ValueRW;
			RebuildSpatialHash(ref state, ref st, ref ecb);
		}

		void RebuildSpatialHash(
			ref SystemState state,
			ref VoxelObjectHash st,
			ref EntityCommandBuffer ecb
		)
		{
			foreach (
				var (objectRef, ltwRef, spatialRef, entity) in Query<
					RefRO<NativeVoxelObject>,
					RefRO<LocalToWorld>,
					RefRO<NeedsSpatialUpdate>
				>()
					.WithEntityAccess()
					.WithAll<NeedsSpatialUpdate, NativeVoxelMesh>() // todo: HasNonEmptyVoxelMesh, adjacent?
			)
			{
				ref readonly var obj = ref objectRef.ValueRO;
				ref readonly var ltw = ref ltwRef.ValueRO;
				st.Add(
					new SpatialVoxelObject
					{
						entity = entity,
						localBounds = obj.localBounds,
						voxelSize = obj.voxelSize,
						ltw = ltw.Value,
						wtl = inverse(ltw.Value),
					}
				);
				ecb.SetComponentEnabled<NeedsSpatialUpdate>(entity, false);
			}
		}

		public struct VoxelObjectHash : IComponentData, IDisposable
		{
			static readonly float3 s_cellSize = EFFECTIVE_CHUNK_SIZE;

			public NativeParallelMultiHashMap<int3, SpatialVoxelObject> hash;

			public void Add(SpatialVoxelObject s)
			{
				// Compute world-space AABB correctly for arbitrary rotations and scales
				var worldBounds = Transform(s.ltw, s.localBounds);

				var cellMin = (int3)floor(worldBounds.Min / s_cellSize);
				var cellMax = (int3)ceil(worldBounds.Max / s_cellSize);

				for (var x = cellMin.x; x <= cellMax.x; x++)
				for (var y = cellMin.y; y <= cellMax.y; y++)
				for (var z = cellMin.z; z <= cellMax.z; z++)
				{
					var k = new int3(x, y, z);

					hash.Add(k, s);

#if ALINE && DEBUG
					if (VoxelDebugging.IsEnabled && VoxelDebugging.Flags.spatialSystemGizmos)
					{
						var min = k * s_cellSize;
						var max = min + s_cellSize;

						var b = new MinMaxAABB(min, max);

						Visual.Draw.WireBox(b.Center, b.Extents, Color.blue);
					}
#endif
				}

#if ALINE && DEBUG
				if (VoxelDebugging.IsEnabled && VoxelDebugging.Flags.spatialSystemGizmos)
				{
					Visual.Draw.WireBox(worldBounds.Center, worldBounds.Extents * 1.01f, Color.green);

					Visual.Draw.PushMatrix(s.ltw);
					Visual.Draw.WireBox(s.localBounds.Center, s.localBounds.Extents * 1.03f, Color.red);
					Visual.Draw.PopMatrix();
				}
#endif
			}

			public NativeList<SpatialVoxelObject> Query(
				MinMaxAABB queryWorldBounds,
				Allocator allocator = Allocator.TempJob
			)
			{
				using var _ = VoxelSpatialSystem_Query.Auto();

				// Cover all spatial hash cells overlapped by the query bounds, not just the center cell.
				var cellMin = (int3)floor(queryWorldBounds.Min / s_cellSize);
				var cellMax = (int3)ceil(queryWorldBounds.Max / s_cellSize);

				NativeList<SpatialVoxelObject> list = new(4, allocator);
				using var seen = new NativeParallelHashSet<Entity>(4, allocator);

				for (var x = cellMin.x; x <= cellMax.x; x++)
				for (var y = cellMin.y; y <= cellMax.y; y++)
				for (var z = cellMin.z; z <= cellMax.z; z++)
				{
					var cell = new int3(x, y, z);
					using var values = hash.GetValuesForKey(cell);
					foreach (var spatialVoxelObject in values)
					{
						// De-duplicate objects that straddle multiple cells
						if (!seen.Add(spatialVoxelObject.entity))
							continue;

						var localObjectBounds = spatialVoxelObject.localBounds;
						var localQueryBounds = Transform(spatialVoxelObject.wtl, queryWorldBounds);

						if (localObjectBounds.Overlaps(localQueryBounds))
							list.Add(spatialVoxelObject);
					}
				}

				return list;
			}

			public void Dispose()
			{
				hash.Dispose();
			}
		}
	}
}
