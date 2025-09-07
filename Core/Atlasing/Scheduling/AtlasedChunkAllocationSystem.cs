namespace Voxels.Core.Atlasing.Scheduling
{
	using Components;
	using Core.Hybrid.GameObjectCollision;
	using Core.Hybrid.GameObjectRendering;
	using Core.Hybrid.GameObjectTransforms;
	using Meshing.Algorithms;
	using Meshing.Components;
	using Meshing.Tags;
	using Procedural;
	using Procedural.Tags;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Logging;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using Unity.Transforms;
	using static Unity.Entities.SystemAPI;
	using static Unity.Mathematics.math;
	using static VoxelConstants;
	using EndInitST = Unity.Entities.EndInitializationEntityCommandBufferSystem.Singleton;
	using Entity = Unity.Entities.Entity;
	using EntityArchetype = Unity.Entities.EntityArchetype;
	using EntityCommandBuffer = Unity.Entities.EntityCommandBuffer;
	using float4x4 = Unity.Mathematics.float4x4;
	using InitializationSystemGroup = Unity.Entities.InitializationSystemGroup;
	using LinkedEntityGroup = Unity.Entities.LinkedEntityGroup;
	using quaternion = Unity.Mathematics.quaternion;
	using SystemState = Unity.Entities.SystemState;

	/// <summary>
	///   Creates and places chunk entities for each grid root with NeedsChunkAllocation enabled.
	///   Chunks are parented to the grid and spaced at EFFECTIVE_CHUNK_SIZE * voxelSize.
	/// </summary>
	[UpdateInGroup(typeof(InitializationSystemGroup))]
	[RequireMatchingQueriesForUpdate]
	// [BurstDiscard]
	public partial struct AtlasedChunkAllocationSystem : ISystem
	{
		EntityArchetype m_ChunkArchetype;

		public void OnCreate(ref SystemState state)
		{
			state.RequireForUpdate<EndInitST>();
			m_ChunkArchetype = state.EntityManager.CreateArchetype(
				typeof(LocalToWorld),
				typeof(NeedsRemesh),
				typeof(AtlasedChunk),
				typeof(HasNonEmptyVoxelMesh),
				typeof(NativeVoxelObject),
				typeof(NeedsSpatialUpdate),
				typeof(NeedsProceduralUpdate),
				typeof(NeedsManagedMeshUpdate),
				typeof(NativeVoxelMesh.Request),
				typeof(ChunkNeedsHybridAllocation),
				typeof(EntityMeshFilterAttachment),
				typeof(EntityMeshColliderAttachment),
				typeof(VoxelMeshingAlgorithmComponent),
				typeof(EntityFollowsGameObjectTransform),
				typeof(PopulateWithProceduralVoxelGenerator)
			);
			// TODO: conditional archetype with (NeedsProceduralUpdate, PopulateWithProceduralVoxelGenerator), if pcg enabled
		}

		[BurstDiscard]
		public void OnUpdate(ref SystemState state)
		{
			var ecb = GetSingleton<EndInitST>().CreateCommandBuffer(state.WorldUnmanaged);

			// TODO: components to track if atlas needs to allocate chunks

			foreach (
				var (atlasRef, ltwRef, entity) in Query<RefRO<NativeChunkAtlas>, RefRO<LocalToWorld>>()
					.WithEntityAccess()
					.WithAll<AtlasNeedsAllocation>()
			)
			{
				Log.Debug("allocating atlas");

				ref readonly var atlas = ref atlasRef.ValueRO;
				ref readonly var ltw = ref ltwRef.ValueRO;

				// Build desired coords
				using var desired = new NativeList<int3>(Allocator.Temp);
				const float chunkStride = EFFECTIVE_CHUNK_SIZE;

				{
					var gridDims = ComputeGridDimensions(ref state, atlas);
					for (var x = 0; x < gridDims.x; x++)
					for (var y = 0; y < gridDims.y; y++)
					for (var z = 0; z < gridDims.z; z++)
						desired.Add(new int3(x, y, z));

					// Trigger initial population: request remesh (and procedural, if configured) on the grid.
					if (HasComponent<NeedsRemesh>(entity))
						ecb.SetComponentEnabled<NeedsRemesh>(entity, true);
					if (HasComponent<NeedsProceduralUpdate>(entity))
						ecb.SetComponentEnabled<NeedsProceduralUpdate>(entity, true);
				}

				// Existing map
				var existing = new NativeParallelHashMap<int3, Entity>(
					max(1, desired.Length * 2),
					Allocator.Temp
				);

				if (state.EntityManager.HasBuffer<LinkedEntityGroup>(entity))
				{
					var leg = state.EntityManager.GetBuffer<LinkedEntityGroup>(entity);
					for (var i = 1; i < leg.Length; i++)
					{
						var ch = leg[i].Value;
						if (!state.EntityManager.HasComponent<AtlasedChunk>(ch))
							continue;
						var c = state.EntityManager.GetComponentData<AtlasedChunk>(ch).coord;
						if (!existing.ContainsKey(c))
							existing.TryAdd(c, ch);
					}
				}

				// Spawn missing
				foreach (var coord in desired)
				{
					if (existing.ContainsKey(coord))
						continue;
					var chunk = CreateChunkEntity(ref ecb, coord, atlas, ltw, atlas.voxelSize, chunkStride);
					ConfigureChunkComponents(ref state, ref ecb, atlas, entity, chunk);
					ecb.AppendToBuffer(entity, new LinkedEntityGroup { Value = chunk });

					var pendingCounter = atlas.counters.Pending().Add(1) + 1;
					atlas.counters.Total().Add(1);
					Log.Verbose("pending++ {0}", pendingCounter);
				}

				ecb.SetComponentEnabled<AtlasNeedsAllocation>(entity, false);
			}
		}

		/// <summary>
		///   Compute grid dimensions based on bounds or rolling grid configuration
		/// </summary>
		static int3 ComputeGridDimensions(ref SystemState state, NativeChunkAtlas chunkAtlas)
		{
			const float chunkStride = EFFECTIVE_CHUNK_SIZE;

			var voxelSize = chunkAtlas.voxelSize;
			var volumeSize = chunkAtlas.bounds.Extents;
			var chunkSize = voxelSize * chunkStride;
			var gridDims = (int3)ceil(volumeSize / chunkSize);

			// Clamp to at least 1 in each axis to avoid zero-chunk grids when bounds are degenerate
			gridDims = max(gridDims, new int3(1));

			return gridDims;
		}

		/// <summary>
		///   Create a chunk entity with basic transform and identification components
		/// </summary>
		Entity CreateChunkEntity(
			ref EntityCommandBuffer ecb,
			int3 coord,
			NativeChunkAtlas chunkAtlas,
			LocalToWorld ltw,
			float voxelSize,
			float chunkStride
		)
		{
			var chunk = ecb.CreateEntity(m_ChunkArchetype);

			ecb.SetComponent(
				chunk,
				new LocalToWorld
				{
					Value = mul(
						ltw.Value,
						float4x4.TRS(
							chunkAtlas.bounds.Min + ((float3)coord * chunkStride * voxelSize),
							quaternion.identity,
							1f
						)
					),
				}
			);

			ecb.SetComponent(
				chunk,
				new AtlasedChunk
				{
					//
					coord = coord,
					atlasId = chunkAtlas.atlasId,
				}
			);
			ecb.SetComponent(
				chunk,
				new NativeVoxelObject
				{
					voxelSize = voxelSize,
					localBounds = new MinMaxAABB(0, chunkStride * voxelSize),
				}
			);
			ecb.SetComponent(chunk, new NativeVoxelMesh.Request { voxelSize = voxelSize });

			return chunk;
		}

		/// <summary>
		///   Configure chunk lifecycle and inherited components
		/// </summary>
		void ConfigureChunkComponents(
			ref SystemState state,
			ref EntityCommandBuffer ecb,
			NativeChunkAtlas atlas,
			Entity gridEntity,
			Entity chunk
		)
		{
			SetupChunkLifecycleComponents(ref ecb, atlas, chunk, ref state);
			InheritGridSettings(ref state, ref ecb, gridEntity, chunk);
		}

		/// <summary>
		///   Setup initial lifecycle component states for chunk
		/// </summary>
		void SetupChunkLifecycleComponents(
			ref EntityCommandBuffer ecb,
			NativeChunkAtlas atlas,
			Entity chunk,
			ref SystemState state
		)
		{
			// set lifecycle tags initial state
			ecb.SetComponentEnabled<NeedsRemesh>(chunk, false);
			ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(chunk, false);

			// For newly allocated chunks, the component exists in the archetype; enable for first schedule
			ecb.SetComponentEnabled<NeedsSpatialUpdate>(chunk, atlas.editable);

			ecb.SetComponentEnabled<ChunkNeedsHybridAllocation>(chunk, true);
		}

		/// <summary>
		///   Copy grid settings to chunk
		/// </summary>
		void InheritGridSettings(
			ref SystemState state,
			ref EntityCommandBuffer ecb,
			Entity gridEntity,
			Entity chunk
		)
		{
			// Copy procedural generator only if present on grid
			if (state.EntityManager.HasComponent<PopulateWithProceduralVoxelGenerator>(gridEntity))
			{
				ecb.SetComponent(
					chunk,
					state.EntityManager.GetComponentData<PopulateWithProceduralVoxelGenerator>(gridEntity)
				);
				ecb.SetComponentEnabled<NeedsProceduralUpdate>(chunk, true);
			}

			ecb.SetComponent(
				chunk,
				state.EntityManager.GetComponentData<VoxelMeshingAlgorithmComponent>(gridEntity)
			);
		}
	}
}
