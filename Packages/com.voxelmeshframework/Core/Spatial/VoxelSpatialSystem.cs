namespace Voxels.Core.Spatial
{
	using System;
	using Authoring;
	using Debugging;
	using Grids;
	using Meshing;
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
	using float4x4 = Unity.Mathematics.float4x4;

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

			st.hash.Clear();

			foreach (
				var (chunkRef, voxelMeshRef, spatialRef, entity) in Query<
					RefRO<NativeVoxelChunk>,
					RefRO<NativeVoxelMesh>,
					RefRO<NeedsSpatialUpdate>
				>()
					.WithEntityAccess()
					.WithAll<NeedsSpatialUpdate>()
			)
			{
				st.Add(
					new SpatialVoxelObject
					{
						entity = entity,
						voxelData = new(voxelMeshRef.ValueRO),
						localBounds = chunkRef.ValueRO.localBounds,
						voxelSize = chunkRef.ValueRO.voxelSize,
						ltw = float4x4.identity, // todo
						wtl = float4x4.identity,
					}
				);

				ecb.SetComponentEnabled<NeedsSpatialUpdate>(entity, spatialRef.ValueRO.persistent);
			}

			foreach (
				var (objectRef, ltwRef, voxelMeshRef, spatialRef, entity) in Query<
					RefRO<NativeVoxelObject>,
					RefRO<LocalToWorld>,
					RefRO<NativeVoxelMesh>,
					RefRO<NeedsSpatialUpdate>
				>()
					.WithEntityAccess()
					.WithAll<NeedsSpatialUpdate>()
			)
			{
				ref readonly var obj = ref objectRef.ValueRO;
				ref readonly var ltw = ref ltwRef.ValueRO;

				st.Add(
					new SpatialVoxelObject
					{
						entity = entity,
						voxelData = new(voxelMeshRef.ValueRO),
						localBounds = obj.localBounds,
						voxelSize = obj.voxelSize,
						ltw = ltw.Value,
						wtl = inverse(ltw.Value),
					}
				);

				ecb.SetComponentEnabled<NeedsSpatialUpdate>(entity, spatialRef.ValueRO.persistent);
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
					hash.Add(new int3(x, y, z), s);

#if ALINE && DEBUG
					if (VoxelDebugging.IsEnabled)
					{
						var min = new int3(x, y, z) * s_cellSize;
						var max = min + s_cellSize;

						var b = new MinMaxAABB(min, max);

						Visual.Draw.PushDuration(.33f);
						Visual.Draw.WireBox(b.Center, b.Extents, Color.blue);
						Visual.Draw.PopDuration();
					}
#endif
				}

#if ALINE && DEBUG
				if (VoxelDebugging.IsEnabled)
				{
					Visual.Draw.PushDuration(.33f);
					Visual.Draw.WireBox(worldBounds.Center, worldBounds.Extents * 1.01f, Color.green);

					Visual.Draw.PushMatrix(s.ltw);
					Visual.Draw.WireBox(s.localBounds.Center, s.localBounds.Extents * 1.03f, Color.red);
					Visual.Draw.PopMatrix();
					Visual.Draw.PopDuration();
				}
#endif
			}

			public NativeArray<SpatialVoxelObject> Query(
				MinMaxAABB queryWorldBounds,
				Allocator allocator = Allocator.TempJob
			)
			{
				using var _ = VoxelSpatialSystem_Query.Auto();

				var cell = (int3)floor(queryWorldBounds.Center / s_cellSize);

				using var values = hash.GetValuesForKey(cell);

				NativeList<SpatialVoxelObject> list = new(1, allocator);
				foreach (var spatialVoxelObject in values)
				{
					var localObjectBounds = spatialVoxelObject.localBounds;
					var localQueryBounds = Transform(spatialVoxelObject.wtl, queryWorldBounds);

					if (localObjectBounds.Overlaps(localQueryBounds))
						list.Add(spatialVoxelObject);
				}

				return list.AsArray();
			}

			public void Dispose()
			{
				hash.Dispose();
			}
		}
	}
}
