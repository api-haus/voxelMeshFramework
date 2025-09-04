namespace Voxels.Core.Grids
{
	using Hybrid;
	using Meshing.Systems;
	using Meshing.Tags;
	using Unity.Entities;
	using Unity.Transforms;
	using UnityEngine;
	using static Unity.Entities.SystemAPI;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;

	/// <summary>
	///   Managed system that instantiates a GameObject per chunk and parents it under the grid host GameObject.
	///   Transforms are not driven by DOTS; the chunk GO is parented to the grid GO directly.
	/// </summary>
	[UpdateInGroup(typeof(SimulationSystemGroup))]
	[UpdateAfter(typeof(ManagedVoxelMeshingSystem))]
	public partial class GridChunkHybridInstantiationSystem : SystemBase
	{
		protected override void OnUpdate()
		{
			// using var _ = GridChunkHybridInstantiationSystem_Update.Auto();
			var ecb = SystemAPI.GetSingleton<EndSimST>().CreateCommandBuffer(World.Unmanaged);

			// For each grid with prefab settings and instance id, instantiate missing chunk GOs
			foreach (
				var (prefabSettings, gridAttach, gridLeg, gridEntity) in Query<
					ChunkPrefabSettings,
					EntityGameObjectInstanceIDAttachment,
					DynamicBuffer<LinkedEntityGroup>
				>()
					.WithAll<NativeVoxelGrid>()
					.WithEntityAccess()
			)
			{
				var rootGo = GetHostGameObject(gridAttach.gameObjectInstanceID);
				if (!rootGo || !prefabSettings.prefab)
					continue;

				for (var i = 1; i < gridLeg.Length; i++)
				{
					var chunk = gridLeg[i].Value;
					if (SystemAPI.HasComponent<ChunkHybridReady>(chunk))
						continue;

					// Instantiate prefab and parent to grid GO
					var go = Object.Instantiate(prefabSettings.prefab, rootGo.transform);
					go.name = $"Chunk_{chunk.Index}";

					var tr = go.transform;

					var lt = EntityManager.GetComponentData<LocalToWorld>(chunk);
					tr.position = lt.Position;
					tr.rotation = lt.Rotation;

					// Attach mesh filter and optional collider to the entity for downstream systems
					if (go.TryGetComponent(out MeshFilter mf))
						ecb.SetComponent(chunk, new EntityMeshFilterAttachment { attachTo = mf });
					if (go.TryGetComponent(out MeshCollider mc))
						ecb.SetComponent(chunk, new EntityMeshColliderAttachment { attachTo = mc });

					ecb.SetComponent(chunk, new EntityGameObjectTransformAttachment { attachTo = tr });

					// Ensure managed mesh update will wire the MeshFilter
					ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(chunk, true);
					ecb.AddComponent<ChunkHybridReady>(chunk);
				}
			}
		}

		static GameObject GetHostGameObject(int instanceId)
		{
			return (GameObject)Resources.InstanceIDToObject(instanceId);
		}
	}
}
