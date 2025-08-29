namespace Voxels.Core.Spatial
{
	using System;
	using Grids;
	using Meshing;
	using Meshing.Tags;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using Unity.Transforms;
	using static Unity.Entities.SystemAPI;
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
						voxelData = new(voxelMeshRef.ValueRO),
						bounds = chunkRef.ValueRO.bounds,
						entity = entity,
						voxelSize = chunkRef.ValueRO.voxelSize,
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
				var position = ltwRef.ValueRO.Position;
				var bounds = objectRef.ValueRO.Bounds(position);

				st.Add(
					new SpatialVoxelObject
					{
						voxelData = new(voxelMeshRef.ValueRO),
						bounds = bounds,
						entity = entity,
						voxelSize = objectRef.ValueRO.voxelSize,
					}
				);

				ecb.SetComponentEnabled<NeedsSpatialUpdate>(entity, spatialRef.ValueRO.persistent);
			}
		}

		public struct VoxelObjectHash : IComponentData, IDisposable
		{
			static readonly float3 s_cellSize = EFFECTIVE_CHUNK_SIZE;

			public NativeParallelMultiHashMap<int3, SpatialVoxelObject> hash;

			public void Add(SpatialVoxelObject svo)
			{
				var cellMin = (int3)floor(svo.bounds.Min / s_cellSize);
				var cellMax = (int3)ceil(svo.bounds.Max / s_cellSize);

				for (var x = cellMin.x; x <= cellMax.x; x++)
				for (var y = cellMin.y; y <= cellMax.y; y++)
				for (var z = cellMin.z; z <= cellMax.z; z++)
					hash.Add(new int3(x, y, z), svo);
			}

			public NativeArray<SpatialVoxelObject> Query(
				MinMaxAABB bounds,
				Allocator allocator = Allocator.TempJob
			)
			{
				var cell = (int3)floor(bounds.Center / s_cellSize);

				using var values = hash.GetValuesForKey(cell);

				NativeList<SpatialVoxelObject> list = new(1, allocator);
				foreach (var spatialVoxelObject in values)
					list.Add(spatialVoxelObject);

				return list.AsArray();
			}

			public void Dispose()
			{
				hash.Dispose();
			}
		}
	}
}
