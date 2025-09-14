namespace Voxels.Core.Hybrid
{
	using System.Collections.Generic;
	using Atlasing.Components;
	using Atlasing.Hybrid;
	using Authoring;
	using GameObjectLifecycle;
	using GameObjectTransforms;
	using Meshing.Algorithms;
	using Procedural;
	using Procedural.Tags;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Transforms;
	using UnityEngine;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Mathematics.math;
	using static VoxelEntityBridge;

	public static class VoxelMeshAtlasToEntity
	{
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
					typeof(ChunkPrefabSettings),
					typeof(AtlasNeedsAllocation),
					typeof(NativeChunkAtlas.CleanupTag),
					typeof(VoxelMeshingAlgorithmComponent),
					typeof(EntityGameObjectInstanceIDAttachment),
				}
			);

			if (attachTransform)
				types.Add(typeof(EntityFollowsGameObjectTransform));

			if (vm.config.voxelGenerator)
			{
				types.Add(typeof(PopulateWithProceduralVoxelGenerator));
				types.Add(typeof(NeedsProceduralUpdate));
			}

			var ent = em.CreateEntity(types.ToArray());

			em.SetName(ent, vm.gameObject.name);

			// Initialize enableable tags on grid root
			em.SetComponentEnabled<AtlasNeedsAllocation>(ent, true);

			em.SetComponentData(
				ent,
				new NativeChunkAtlas
				{
					//
					editable = vm.config.stampEditable,
					atlasId = instanceId,
					voxelSize = vm.config.voxelSize,
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

			// Provide chunk prefab + material settings for hybrid instantiation
			em.SetComponentData(ent, new ChunkPrefabSettings { prefab = vm.chunkPrefab });

			em.SetComponentData(
				ent,
				new VoxelMeshingAlgorithmComponent
				{
					algorithm = vm.config.meshingAlgorithm,
					normalsMode = vm.config.normalsMode,
					materialEncoding = vm.config.materialEncoding,
				}
			);

			if (attachTransform)
				em.SetComponentData(
					ent,
					new EntityFollowsGameObjectTransform { attachTo = attachTransform }
				);

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

			// Ensure LinkedEntityGroup buffer exists and contains root for lifecycle management
			{
				var leg = em.GetBuffer<LinkedEntityGroup>(ent);
				leg.Add(ent);
			}

			return ent;
		}
	}
}
