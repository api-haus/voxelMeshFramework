namespace Voxels.Core.Grids
{
	using Hybrid;
	using Meshing;
	using Meshing.Tags;
	using Procedural;
	using Procedural.Tags;
	using Unity.Burst;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using Unity.Transforms;
	using static Unity.Entities.SystemAPI;
	using static Unity.Mathematics.math;
	using static VoxelConstants;
	using EndInitST = Unity.Entities.EndInitializationEntityCommandBufferSystem.Singleton;
	using float4x4 = Unity.Mathematics.float4x4;
	using quaternion = Unity.Mathematics.quaternion;

	/// <summary>
	///   Creates and places chunk entities for each grid root with NeedsChunkAllocation enabled.
	///   Chunks are parented to the grid and spaced at EFFECTIVE_CHUNK_SIZE * voxelSize.
	/// </summary>
	[UpdateInGroup(typeof(InitializationSystemGroup))]
	[RequireMatchingQueriesForUpdate]
	public partial struct GridChunkAllocationSystem : ISystem
	{
		EntityArchetype m_ChunkArchetype;

		public void OnCreate(ref SystemState state)
		{
			state.RequireForUpdate<EndInitST>();
			m_ChunkArchetype = state.EntityManager.CreateArchetype(
				// typeof(Parent),
				typeof(NeedsRemesh),
				typeof(LocalToWorld),
				// typeof(LocalTransform),
				typeof(NativeVoxelChunk),
				typeof(NativeVoxelObject),
				typeof(NeedsSpatialUpdate),
				typeof(NeedsProceduralUpdate),
				typeof(NeedsManagedMeshUpdate),
				typeof(NativeVoxelMesh.Request),
				typeof(EntityMeshFilterAttachment),
				typeof(EntityMeshColliderAttachment),
				typeof(VoxelMeshingAlgorithmComponent),
				typeof(EntityGameObjectTransformAttachment),
				typeof(PopulateWithProceduralVoxelGenerator)
			);
			// TODO: conditional archetype with (NeedsProceduralUpdate, PopulateWithProceduralVoxelGenerator), if pcg enabled
		}

		[BurstDiscard] // TODO:
		public void OnUpdate(ref SystemState state)
		{
			var ecb = GetSingleton<EndInitST>().CreateCommandBuffer(state.WorldUnmanaged);

			const float chunkStride = EFFECTIVE_CHUNK_SIZE;

			foreach (
				var (gridRef, ltwRef, entity) in Query<RefRO<NativeVoxelGrid>, RefRO<LocalToWorld>>()
					.WithAll<NeedsChunkAllocation>()
					.WithEntityAccess()
			)
			{
				ref readonly var grid = ref gridRef.ValueRO;
				ref readonly var ltw = ref ltwRef.ValueRO;

				var voxelSize = grid.voxelSize;
				int3 gridDims;
				var size = grid.bounds.Max - grid.bounds.Min;
				gridDims = (int3)ceil(size / (voxelSize * chunkStride));
				if (state.EntityManager.HasComponent<RollingGridConfig>(entity))
				{
					var cfg = state.EntityManager.GetComponentData<RollingGridConfig>(entity);
					if (cfg.enabled)
						gridDims = cfg.slotDims;
				}
				gridDims = min(gridDims, new int3(64));

				// ensure LinkedEntityGroup exists and contains root (created in bridge)
				var leg = GetBuffer<LinkedEntityGroup>(entity);

				for (var x = 0; x < gridDims.x; x++)
				for (var y = 0; y < gridDims.y; y++)
				for (var z = 0; z < gridDims.z; z++)
				{
					int3 coord = new(x, y, z);

					var chunk = ecb.CreateEntity(m_ChunkArchetype);

					// ecb.SetComponent(
					// 	chunk,
					// 	new LocalTransform
					// 	{
					// 		Position = grid.bounds.Min + ((float3)coord * chunkStride * voxelSize),
					// 		Rotation = quaternion.identity,
					// 		Scale = 1f,
					// 	}
					// );
					// ecb.SetComponent(chunk, new Parent { Value = entity });

					ecb.SetComponent(
						chunk,
						new LocalToWorld
						{
							Value = mul(
								ltw.Value,
								float4x4.TRS(
									grid.bounds.Min + ((float3)coord * chunkStride * voxelSize),
									quaternion.identity,
									1f
								)
							),
						}
					);
					ecb.SetComponent(chunk, new NativeVoxelChunk { coord = coord, gridID = grid.gridID });
					ecb.SetComponent(
						chunk,
						new NativeVoxelObject
						{
							voxelSize = voxelSize,
							localBounds = new MinMaxAABB(0, chunkStride * voxelSize),
						}
					);
					ecb.SetComponent(chunk, new NativeVoxelMesh.Request { voxelSize = voxelSize });

					// set lifecycle tags initial state (disabled by default)
					ecb.SetComponentEnabled<NeedsRemesh>(chunk, false);
					ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(chunk, false);
					ecb.SetComponent(chunk, new NeedsSpatialUpdate { persistent = true });
					ecb.SetComponentEnabled<NeedsSpatialUpdate>(chunk, true);

					// inherit grid Needs*
					ecb.SetComponentEnabled<NeedsRemesh>(
						chunk,
						HasComponent<NeedsRemesh>(entity) && IsComponentEnabled<NeedsRemesh>(entity)
					);
					ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(
						chunk,
						HasComponent<NeedsManagedMeshUpdate>(entity)
							&& IsComponentEnabled<NeedsManagedMeshUpdate>(entity)
					);
					if (HasComponent<NeedsProceduralUpdate>(entity))
						ecb.SetComponentEnabled<NeedsProceduralUpdate>(
							chunk,
							IsComponentEnabled<NeedsProceduralUpdate>(entity)
						);

					// copy settings
					ecb.SetComponent(
						chunk,
						state.EntityManager.GetComponentData<PopulateWithProceduralVoxelGenerator>(entity)
					);
					ecb.SetComponent(
						chunk,
						state.EntityManager.GetComponentData<VoxelMeshingAlgorithmComponent>(entity)
					);

					// link under grid for lifetime management
					ecb.AppendToBuffer(entity, new LinkedEntityGroup { Value = chunk });
				}

				// set grid progress totals on first allocation
				{
					var total = gridDims.x * gridDims.y * gridDims.z;
					if (!state.EntityManager.HasComponent<GridMeshingProgress>(entity))
						ecb.AddComponent<GridMeshingProgress>(entity);
					var prog = state.EntityManager.HasComponent<GridMeshingProgress>(entity)
						? state.EntityManager.GetComponentData<GridMeshingProgress>(entity)
						: default;
					prog.totalChunks = total;
					prog.allocatedChunks = total;
					ecb.SetComponent(entity, prog);
				}

				ecb.SetComponentEnabled<NeedsChunkAllocation>(entity, false);
			}
		}
	}
}
