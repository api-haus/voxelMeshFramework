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
	using Unity.Entities;
	using Unity.Logging;
	using Unity.Mathematics.Geometry;
	using Unity.Transforms;
	using UnityEngine;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Mathematics.math;
	using static VoxelConstants;

	static class VoxelEntityBridge
	{
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
