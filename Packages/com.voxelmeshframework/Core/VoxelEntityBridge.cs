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
			if (!world.IsCreated)
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
					enableFairing = vm.enableFairing,
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
			this VoxelMeshGrid vmg,
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
				}
			);

			if (attachTransform)
				types.Add(typeof(EntityGameObjectTransformAttachment));

			if (vmg.procedural)
			{
				types.Add(typeof(PopulateWithProceduralVoxelGenerator));
				types.Add(typeof(NeedsProceduralUpdate));
			}

			var ent = em.CreateEntity(types.ToArray());

			em.SetComponentData(
				ent,
				new NativeVoxelGrid
				{
					//
					gridID = instanceId,
					voxelSize = vmg.voxelSize,
					bounds = new(vmg.worldBounds.min, vmg.worldBounds.max),
				}
			);
			em.SetComponentData(
				ent,
				new EntityGameObjectInstanceIDAttachment { gameObjectInstanceID = instanceId }
			);
			em.SetComponentData(ent, new LocalToWorld { Value = vmg.transform.localToWorldMatrix });
			em.SetComponentData(
				ent,
				new LocalTransform
				{
					Position = vmg.transform.position,
					Rotation = vmg.transform.rotation,
					Scale = cmax(vmg.transform.localScale),
				}
			);
			em.SetComponentData(ent, new NeedsSpatialUpdate { persistent = attachTransform });

			if (attachTransform)
				em.SetComponentData(
					ent,
					new EntityGameObjectTransformAttachment { attachTo = attachTransform }
				);
			if (vmg.procedural)
			{
				em.SetComponentData(
					ent,
					new PopulateWithProceduralVoxelGenerator { generator = vmg.procedural }
				);
				em.SetComponentEnabled<NeedsProceduralUpdate>(ent, true);
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
