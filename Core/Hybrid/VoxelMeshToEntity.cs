namespace Voxels.Core.Hybrid
{
	using System.Collections.Generic;
	using Atlasing.Components;
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
	using Unity.Entities;
	using Unity.Mathematics.Geometry;
	using Unity.Transforms;
	using UnityEngine;
	using static Diagnostics.VoxelProfiler.Marks;
	using static VoxelConstants;
	using static VoxelEntityBridge;

	public static class VoxelMeshToEntity
	{
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
					typeof(HasNonEmptyVoxelMesh),
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

			if (vm.config.voxelGenerator)
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
					voxelSize = vm.config.voxelSize,
					localBounds = new MinMaxAABB(0, EFFECTIVE_CHUNK_SIZE * vm.config.voxelSize),
				}
			);
			em.SetComponentData(
				ent,
				new NativeVoxelMesh.Request
				{
					//
					voxelSize = vm.config.voxelSize,
				}
			);
			em.SetComponentData(
				ent,
				new VoxelMeshingAlgorithmComponent
				{
					algorithm = vm.config.meshingAlgorithm,
					normalsMode = vm.config.normalsMode,
					materialEncoding = vm.config.materialEncoding,
				}
			);

			em.SetComponentData(
				ent,
				new EntityMeshFilterAttachment { attachTo = vm.GetComponent<MeshFilter>() }
			);
			em.SetComponentData(ent, new LocalToWorld { Value = vm.transform.localToWorldMatrix });
			em.SetComponentData(ent, new NeedsSpatialUpdate());

			if (attachTransform)
				em.SetComponentData(
					ent,
					new EntityFollowsGameObjectTransform { attachTo = attachTransform }
				);
			if (meshCollider)
				em.SetComponentData(ent, new EntityMeshColliderAttachment { attachTo = meshCollider });
			if (vm.config.voxelGenerator)
			{
				em.SetComponentData(
					ent,
					new PopulateWithProceduralVoxelGenerator
					{
						voxels = vm.config.voxelGenerator,
						materials = vm.config.materialGenerator,
					}
				);
				em.SetComponentEnabled<NeedsProceduralUpdate>(ent, true);
			}

			return ent;
		}
	}
}
