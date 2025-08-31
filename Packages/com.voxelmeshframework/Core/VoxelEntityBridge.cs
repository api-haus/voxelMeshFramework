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
	using Unity.Transforms;
	using UnityEngine;
	using static Unity.Mathematics.math;

	static class VoxelEntityBridge
	{
		static EntityManager EntityManager =>
			World.DefaultGameObjectInjectionWorld?.EntityManager ?? default;

		// TODO: archetypes
		public static Entity CreateVoxelMeshEntity(
			this VoxelMesh vm,
			int instanceId,
			Transform attachTransform = null
		)
		{
			List<ComponentType> types = new(
				new ComponentType[]
				{
					typeof(EntityGameObjectInstanceIDAttachment),
					typeof(NativeVoxelObject),
					typeof(NativeVoxelMesh.Request),
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

			var ent = EntityManager.CreateEntity(types.ToArray());

			EntityManager.SetComponentEnabled<NeedsManagedMeshUpdate>(ent, false);
			EntityManager.SetComponentEnabled<NeedsSpatialUpdate>(ent, true);
			EntityManager.SetComponentEnabled<NeedsRemesh>(ent, false);
			EntityManager.SetComponentData(
				ent,
				new EntityGameObjectInstanceIDAttachment { gameObjectInstanceID = instanceId }
			);
			EntityManager.SetComponentData(
				ent,
				new NativeVoxelObject
				{
					//
					voxelSize = vm.voxelSize,
				}
			);
			EntityManager.SetComponentData(
				ent,
				new NativeVoxelMesh.Request
				{
					//
					voxelSize = vm.voxelSize,
				}
			);

			EntityManager.SetComponentData(
				ent,
				new EntityMeshFilterAttachment { attachTo = vm.GetComponent<MeshFilter>() }
			);
			EntityManager.SetComponentData(
				ent,
				new LocalToWorld { Value = vm.transform.localToWorldMatrix }
			);
			EntityManager.SetComponentData(ent, new NeedsSpatialUpdate { persistent = attachTransform });

			if (attachTransform)
				EntityManager.SetComponentData(
					ent,
					new EntityGameObjectTransformAttachment { attachTo = attachTransform }
				);
			if (meshCollider)
				EntityManager.SetComponentData(
					ent,
					new EntityMeshColliderAttachment { attachTo = meshCollider }
				);
			if (vm.procedural)
			{
				EntityManager.SetComponentData(
					ent,
					new PopulateWithProceduralVoxelGenerator { generator = vm.procedural }
				);
				EntityManager.SetComponentEnabled<NeedsProceduralUpdate>(ent, true);
			}

			return ent;
		}

		public static Entity CreateVoxelMeshGridEntity(
			this VoxelMeshGrid vmg,
			int instanceId,
			Transform attachTransform = null
		)
		{
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

			var ent = EntityManager.CreateEntity(types.ToArray());

			EntityManager.SetComponentData(
				ent,
				new NativeVoxelGrid
				{
					//
					gridID = instanceId,
					voxelSize = vmg.voxelSize,
					bounds = new(vmg.worldBounds.min, vmg.worldBounds.max),
				}
			);
			EntityManager.SetComponentData(
				ent,
				new EntityGameObjectInstanceIDAttachment { gameObjectInstanceID = instanceId }
			);
			EntityManager.SetComponentData(
				ent,
				new LocalToWorld { Value = vmg.transform.localToWorldMatrix }
			);
			EntityManager.SetComponentData(
				ent,
				new LocalTransform
				{
					Position = vmg.transform.position,
					Rotation = vmg.transform.rotation,
					Scale = cmax(vmg.transform.localScale),
				}
			);
			EntityManager.SetComponentData(ent, new NeedsSpatialUpdate { persistent = attachTransform });

			if (attachTransform)
				EntityManager.SetComponentData(
					ent,
					new EntityGameObjectTransformAttachment { attachTo = attachTransform }
				);
			if (vmg.procedural)
			{
				EntityManager.SetComponentData(
					ent,
					new PopulateWithProceduralVoxelGenerator { generator = vmg.procedural }
				);
				EntityManager.SetComponentEnabled<NeedsProceduralUpdate>(ent, true);
			}

			return ent;
		}

		public static void DestroyEntityByInstanceID(int gameObjectInstanceID)
		{
			if (EntityManager.Equals(default))
				return;

			var entity = EntityManager.CreateEntity(typeof(DestroyEntityByInstanceIDEvent));

			EntityManager.SetComponentData(
				entity,
				new DestroyEntityByInstanceIDEvent { gameObjectInstanceID = gameObjectInstanceID }
			);
		}

		public static void DestroyEntity(Entity ent)
		{
			if (EntityManager.Equals(default))
				return;
			EntityManager.DestroyEntity(ent);
		}
	}
}
