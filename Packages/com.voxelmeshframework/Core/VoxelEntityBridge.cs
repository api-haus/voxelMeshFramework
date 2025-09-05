namespace Voxels.Core
{
	using System.Collections.Generic;
	using Authoring;
	using Grids;
	using Hybrid;
	using Meshing;
	using Meshing.Tags;
	using Procedural;
	using Procedural.Tags;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Logging;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using Unity.Transforms;
	using UnityEngine;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Mathematics.math;
	using static VoxelConstants;

	static class VoxelEntityBridge
	{
		public static bool TryGetGridEntity(int gameObjectInstanceID, out Entity entity)
		{
			entity = Entity.Null;
			if (!TryGetEntityManager(out var em))
				return false;
			// 1) Fast path: use EntityInstanceIDLifecycleSystem singleton hashmap
			{
				var mapQuery = em.CreateEntityQuery(ComponentType.ReadOnly<EntityByInstanceID>());
				if (!mapQuery.IsEmptyIgnoreFilter)
				{
					var by = mapQuery.GetSingleton<EntityByInstanceID>();
					if (by.map.IsCreated && by.map.TryGetValue(gameObjectInstanceID, out var e))
						if (em.HasComponent<NativeVoxelGrid>(e))
						{
							entity = e;
							return true;
						}
				}
			}

			// 2) Fallback: direct query (covers first-frame race before hashmap updates)
			{
				var q = em.CreateEntityQuery(
					ComponentType.ReadOnly<EntityGameObjectInstanceIDAttachment>(),
					ComponentType.ReadOnly<NativeVoxelGrid>()
				);
				using var ents = q.ToEntityArray(Allocator.Temp);
				for (var i = 0; i < ents.Length; i++)
				{
					var e = ents[i];
					var at = em.GetComponentData<EntityGameObjectInstanceIDAttachment>(e);
					if (at.gameObjectInstanceID == gameObjectInstanceID)
					{
						entity = e;
						return true;
					}
				}
			}

			return false;
		}

		public static void SendRollingMovementRequest(
			int gridGameObjectInstanceID,
			int3 targetAnchorWorldChunk
		)
		{
			if (!TryGetEntityManager(out var em))
				return;
			if (!TryGetGridEntity(gridGameObjectInstanceID, out var ent))
				return;
			Log.Debug(
				"[Bridge] Move request: gid={gid} targetAnchor={anchor}",
				gridGameObjectInstanceID,
				targetAnchorWorldChunk
			);
			// Force-enable rolling when receiving movement requests to avoid early Awake() races
			EnsureRollingConfigPrecomputed(em, ent, true);
			if (!em.HasComponent<RollingGridMoveRequest>(ent))
				em.AddComponent<RollingGridMoveRequest>(ent);
			var req = em.GetComponentData<RollingGridMoveRequest>(ent);
			req.targetAnchorWorldChunk = targetAnchorWorldChunk;
			em.SetComponentData(ent, req);
			em.SetComponentEnabled<RollingGridMoveRequest>(ent, true);
		}

		public static void EnableRollingForGrid(int gameObjectInstanceID)
		{
			if (!TryGetEntityManager(out var em))
				return;
			if (!TryGetGridEntity(gameObjectInstanceID, out var ent))
				return;
			EnsureRollingConfigPrecomputed(em, ent, true);
		}

		static void EnsureRollingConfigPrecomputed(
			EntityManager em,
			Entity gridEntity,
			bool forceEnable = false
		)
		{
			var grid = em.GetComponentData<NativeVoxelGrid>(gridEntity);
			var stride = grid.voxelSize * EFFECTIVE_CHUNK_SIZE;
			var size = grid.bounds.Max - grid.bounds.Min;
			var dims = (int3)ceil(size / stride);
			if (!em.HasComponent<RollingGridConfig>(gridEntity))
				em.AddComponent<RollingGridConfig>(gridEntity);
			var cfg = em.GetComponentData<RollingGridConfig>(gridEntity);
			cfg.enabled = forceEnable || cfg.enabled;
			cfg.slotDims = dims;
			em.SetComponentData(gridEntity, cfg);
			Log.Debug(
				"[Bridge] Ensure rolling config: gid={gid} enabled={enabled} slotDims={dims}",
				grid.gridID,
				cfg.enabled,
				dims
			);
		}

		public static bool TryGetEntityManager(out EntityManager em)
		{
			using var _ = VoxelEntityBridge_TryGetEntityManager.Auto();
			em = default;
			var world = World.DefaultGameObjectInjectionWorld;
			if (world is not { IsCreated: true })
			{
				Log.Verbose("DefaultGameObjectInjectionWorld is not created!");
				return false;
			}

			em = world.EntityManager;
			return true;
		}

		// TODO: archetypes
		public static Entity CreateVoxelMeshEntity(
			this VoxelMesh vm,
			int instanceId,
			Transform attachTransform = null
		)
		{
			if (!TryGetEntityManager(out var em))
				return Entity.Null;

			using var _ = VoxelEntityBridge_CreateMeshEntity.Auto();
			List<ComponentType> types = new(
				new ComponentType[]
				{
					typeof(EntityGameObjectInstanceIDAttachment),
					typeof(NativeVoxelObject),
					typeof(NativeVoxelMesh.Request),
					typeof(VoxelMeshingAlgorithmComponent),
					typeof(NeedsManagedMeshUpdate),
					typeof(NeedsSpatialUpdate),
					typeof(NeedsRemesh),
					typeof(EntityMeshFilterAttachment),
					typeof(LocalToWorld),
				}
			);

			if (attachTransform)
				types.Add(typeof(EntityGameObjectTransformAttachment));

			vm.TryGetComponent(out MeshCollider meshCollider);

			if (meshCollider)
				types.Add(typeof(EntityMeshColliderAttachment));

			if (vm.procedural)
			{
				types.Add(typeof(PopulateWithProceduralVoxelGenerator));
				types.Add(typeof(NeedsProceduralUpdate));
			}

			var ent = em.CreateEntity(types.ToArray());

			em.SetComponentEnabled<NeedsManagedMeshUpdate>(ent, false);
			em.SetComponentEnabled<NeedsSpatialUpdate>(ent, true);
			em.SetComponentEnabled<NeedsRemesh>(ent, false);
			em.SetComponentData(
				ent,
				new EntityGameObjectInstanceIDAttachment { gameObjectInstanceID = instanceId }
			);
			em.SetComponentData(
				ent,
				new NativeVoxelObject
				{
					//
					voxelSize = vm.voxelSize,
					localBounds = new MinMaxAABB(0, EFFECTIVE_CHUNK_SIZE * vm.voxelSize),
				}
			);
			em.SetComponentData(
				ent,
				new NativeVoxelMesh.Request
				{
					//
					voxelSize = vm.voxelSize,
				}
			);
			em.SetComponentData(
				ent,
				new VoxelMeshingAlgorithmComponent
				{
					algorithm = vm.meshingAlgorithm,
					normalsMode = vm.normalsMode,
					fairingIterations = vm.fairingIterations,
					fairingStepSize = vm.fairingStepSize,
					cellMargin = vm.cellMargin,
					recomputeNormalsAfterFairing = vm.recomputeNormalsAfterFairing,
					materialDistributionMode = vm.materialDistributionMode,
				}
			);

			em.SetComponentData(
				ent,
				new EntityMeshFilterAttachment { attachTo = vm.GetComponent<MeshFilter>() }
			);
			em.SetComponentData(ent, new LocalToWorld { Value = vm.transform.localToWorldMatrix });
			em.SetComponentData(ent, new NeedsSpatialUpdate { persistent = attachTransform });

			if (attachTransform)
				em.SetComponentData(
					ent,
					new EntityGameObjectTransformAttachment { attachTo = attachTransform }
				);
			if (meshCollider)
				em.SetComponentData(ent, new EntityMeshColliderAttachment { attachTo = meshCollider });
			if (vm.procedural)
			{
				em.SetComponentData(
					ent,
					new PopulateWithProceduralVoxelGenerator { generator = vm.procedural }
				);
				em.SetComponentEnabled<NeedsProceduralUpdate>(ent, true);
			}

			return ent;
		}

		public static Entity CreateVoxelMeshGridEntity(
			this VoxelMeshGrid vm,
			int instanceId,
			Transform attachTransform = null
		)
		{
			if (!TryGetEntityManager(out var em))
				return Entity.Null;

			using var _ = VoxelEntityBridge_CreateGridEntity.Auto();
			List<ComponentType> types = new(
				new ComponentType[]
				{
					typeof(LocalToWorld),
					typeof(LocalTransform),
					typeof(NativeVoxelGrid),
					typeof(NativeVoxelGrid.MeshingBudget),
					typeof(GridMeshingProgress),
					typeof(NativeGridMeshingCounters),
					typeof(NativeVoxelGrid.FullyMeshedEvent),
					typeof(EntityGameObjectInstanceIDAttachment),
					typeof(NeedsRemesh),
					typeof(NeedsManagedMeshUpdate),
					typeof(NeedsSpatialUpdate),
					typeof(NeedsChunkAllocation),
					typeof(ChunkPrefabSettings),
					typeof(VoxelMeshingAlgorithmComponent),
				}
			);

			if (attachTransform)
				types.Add(typeof(EntityGameObjectTransformAttachment));

			if (vm.procedural)
			{
				types.Add(typeof(PopulateWithProceduralVoxelGenerator));
				types.Add(typeof(NeedsProceduralUpdate));
			}

			var ent = em.CreateEntity(types.ToArray());

			// Initialize enableable tags on grid root
			em.SetComponentEnabled<NeedsRemesh>(ent, false);
			em.SetComponentEnabled<NeedsSpatialUpdate>(ent, false);
			em.SetComponentEnabled<NeedsChunkAllocation>(ent, true);
			em.SetComponentEnabled<NeedsManagedMeshUpdate>(ent, false);
			em.SetComponentEnabled<NativeVoxelGrid.FullyMeshedEvent>(ent, false);

			em.SetComponentData(
				ent,
				new NativeVoxelGrid
				{
					//
					gridID = instanceId,
					voxelSize = vm.voxelSize,
					bounds = new(vm.worldBounds.min, vm.worldBounds.max),
				}
			);
			em.SetComponentData(
				ent,
				new EntityGameObjectInstanceIDAttachment { gameObjectInstanceID = instanceId }
			);
			em.SetComponentData(ent, new LocalToWorld { Value = vm.transform.localToWorldMatrix });
			em.SetComponentData(
				ent,
				new LocalTransform
				{
					Position = vm.transform.position,
					Rotation = vm.transform.rotation,
					Scale = cmax(vm.transform.localScale),
				}
			);
			em.SetComponentData(ent, new NeedsSpatialUpdate { persistent = attachTransform });

			// Defaults per meshing budget and progress
			em.SetComponentData(ent, new NativeVoxelGrid.MeshingBudget { maxMeshesPerFrame = 2 });
			em.SetComponentData(
				ent,
				new GridMeshingProgress
				{
					totalChunks = 0,
					allocatedChunks = 0,
					processedCount = 0,
					meshedOnceCount = 0,
					firedOnce = false,
				}
			);
			// Allocate in-flight counter storage
			{
				var counters = new NativeGridMeshingCounters
				{
					inFlight = new Unity.Collections.NativeReference<int>(
						0,
						Unity.Collections.Allocator.Persistent
					),
				};
				em.SetComponentData(ent, counters);
			}

			// Provide chunk prefab + material settings for hybrid instantiation
			em.SetComponentData(
				ent,
				new ChunkPrefabSettings { prefab = vm.chunkPrefab, defaultMaterial = vm.surfaceMaterial }
			);

			em.SetComponentData(
				ent,
				new VoxelMeshingAlgorithmComponent
				{
					algorithm = vm.meshingAlgorithm,
					normalsMode = vm.normalsMode,
					fairingIterations = vm.fairingIterations,
					fairingStepSize = vm.fairingStepSize,
					cellMargin = vm.cellMargin,
					recomputeNormalsAfterFairing = vm.recomputeNormalsAfterFairing,
					materialDistributionMode = vm.materialDistributionMode,
				}
			);
			if (attachTransform)
				em.SetComponentData(
					ent,
					new EntityGameObjectTransformAttachment { attachTo = attachTransform }
				);
			if (vm.procedural)
			{
				em.SetComponentData(
					ent,
					new PopulateWithProceduralVoxelGenerator { generator = vm.procedural }
				);
				em.SetComponentEnabled<NeedsProceduralUpdate>(ent, true);
			}

			// Ensure LinkedEntityGroup buffer exists and contains root for lifecycle management
			{
				var leg = em.AddBuffer<LinkedEntityGroup>(ent);
				leg.Add(ent);
			}

			return ent;
		}

		public static void DestroyEntityByInstanceID(int gameObjectInstanceID)
		{
			if (!TryGetEntityManager(out var em))
				return;

			if (em.Equals(default))
				return;
			var entity = em.CreateEntity(typeof(DestroyEntityByInstanceIDEvent));

			em.SetComponentData(
				entity,
				new DestroyEntityByInstanceIDEvent { gameObjectInstanceID = gameObjectInstanceID }
			);
		}

		public static void DestroyEntity(Entity ent)
		{
			if (!TryGetEntityManager(out var em))
				return;

			em.DestroyEntity(ent);
		}
	}
}
