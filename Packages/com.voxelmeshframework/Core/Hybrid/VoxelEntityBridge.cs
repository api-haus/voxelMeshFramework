namespace Voxels.Core.Hybrid
{
	using System.Collections.Generic;
	using Atlasing.Components;
	using Atlasing.Hybrid;
	using Authoring;
	using GameObjectCollision;
	using GameObjectLifecycle;
	using GameObjectRendering;
	using GameObjectTransforms;
	using Meshing.Algorithms;
	using Meshing.Components;
	using Meshing.Tags;
	using Procedural;
	using Procedural.Tags;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Logging;
	using Unity.Mathematics.Geometry;
	using Unity.Transforms;
	using UnityEngine;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Mathematics.math;
	using static VoxelConstants;

	public static class VoxelEntityBridge
	{
		public static bool TryGetEntity(int gameObjectInstanceID, out Entity entity)
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
						if (em.HasComponent<NativeChunkAtlas>(e))
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
					ComponentType.ReadOnly<NativeChunkAtlas>()
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

		public static bool TryGetEntityManager(out EntityManager em)
		{
			using var _ = VoxelEntityBridge_TryGetEntityManager.Auto();
			em = default;
			var world = World.DefaultGameObjectInjectionWorld;
			if (world is not { IsCreated: true })
			{
				Log.Warning("DefaultGameObjectInjectionWorld is not created!");
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
					typeof(NeedsRemesh),
					typeof(LocalToWorld),
					typeof(NativeVoxelObject),
					typeof(NeedsSpatialUpdate),
					typeof(NeedsManagedMeshUpdate),
					typeof(NativeVoxelMesh.Request),
					typeof(EntityMeshFilterAttachment),
					typeof(VoxelMeshingAlgorithmComponent),
					typeof(EntityGameObjectInstanceIDAttachment),
				}
			);

			if (attachTransform)
				types.Add(typeof(EntityFollowsGameObjectTransform));

			vm.TryGetComponent(out MeshCollider meshCollider);
			if (meshCollider)
				types.Add(typeof(EntityMeshColliderAttachment));

			if (vm.procedural)
			{
				types.Add(typeof(PopulateWithProceduralVoxelGenerator));
				types.Add(typeof(NeedsProceduralUpdate));
			}

			var ent = em.CreateEntity(types.ToArray());

			em.SetName(ent, vm.gameObject.name);

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
					materialEncoding = vm.materialEncoding,
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
					new EntityFollowsGameObjectTransform { attachTo = attachTransform }
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

		public static Entity CreateAtlasEntity(
			this VoxelMeshAtlas vm,
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
					typeof(NativeChunkAtlas),
					typeof(LinkedEntityGroup),
					typeof(NeedsSpatialUpdate),
					typeof(ChunkPrefabSettings),
					typeof(AtlasNeedsAllocation),
					typeof(NativeChunkAtlas.CleanupTag),
					typeof(VoxelMeshingAlgorithmComponent),
					typeof(EntityGameObjectInstanceIDAttachment),
				}
			);

			if (attachTransform)
				types.Add(typeof(EntityFollowsGameObjectTransform));

			if (vm.proceduralGenerator)
			{
				types.Add(typeof(PopulateWithProceduralVoxelGenerator));
				types.Add(typeof(NeedsProceduralUpdate));
			}

			var ent = em.CreateEntity(types.ToArray());

			em.SetName(ent, vm.gameObject.name);

			// Initialize enableable tags on grid root
			em.SetComponentEnabled<NeedsSpatialUpdate>(ent, false);
			em.SetComponentEnabled<AtlasNeedsAllocation>(ent, true);

			em.SetComponentData(
				ent,
				new NativeChunkAtlas
				{
					//
					atlasId = instanceId,
					voxelSize = vm.voxelSize,
					bounds = new(vm.gridBounds.min, vm.gridBounds.max),
					counters = new(Allocator.Persistent),
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

			// Provide chunk prefab + material settings for hybrid instantiation
			em.SetComponentData(ent, new ChunkPrefabSettings { prefab = vm.chunkPrefab });

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
					materialEncoding = vm.materialEncoding,
				}
			);

			if (attachTransform)
				em.SetComponentData(
					ent,
					new EntityFollowsGameObjectTransform { attachTo = attachTransform }
				);

			if (vm.proceduralGenerator)
			{
				em.SetComponentData(
					ent,
					new PopulateWithProceduralVoxelGenerator { generator = vm.proceduralGenerator }
				);
				em.SetComponentEnabled<NeedsProceduralUpdate>(ent, true);
			}

			// Ensure LinkedEntityGroup buffer exists and contains root for lifecycle management
			{
				var leg = em.GetBuffer<LinkedEntityGroup>(ent);
				leg.Add(ent);
			}

			return ent;
		}

		/// <summary>
		///   Destroy entity by associated instance id, allowing for structural changes to invalidate entity.
		/// </summary>
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

		public static bool TryGetAtlas(int instanceId, out NativeChunkAtlas nativeChunkAtlas)
		{
			nativeChunkAtlas = default;
			if (!TryGetEntityManager(out var em))
				return false;
			if (!TryGetEntity(instanceId, out var ent))
				return false;

			nativeChunkAtlas = em.GetComponentData<NativeChunkAtlas>(ent);
			return true;
		}

		public static bool TryGetAtlas(
			VoxelMeshAtlas atlasAuthor,
			out NativeChunkAtlas nativeChunkAtlas
		)
		{
			return TryGetAtlas(atlasAuthor.gameObject.GetInstanceID(), out nativeChunkAtlas);
		}
	}
}
