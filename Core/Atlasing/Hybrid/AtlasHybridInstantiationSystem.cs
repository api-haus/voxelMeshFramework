namespace Voxels.Core.Atlasing.Hybrid
{
	using Components;
	using Core.Hybrid;
	using Core.Hybrid.GameObjectCollision;
	using Core.Hybrid.GameObjectRendering;
	using Core.Hybrid.GameObjectTransforms;
	using Diagnostics;
	using Meshing.Budgets;
	using Unity.Entities;
	using Unity.Transforms;
	using UnityEngine;
	using static Unity.Entities.SystemAPI;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;
	using ManagedVoxelMeshingSystem = Meshing.Managed.ManagedVoxelMeshingSystem;

	/// <summary>
	///   Managed system that instantiates a GameObject per chunk and parents it under the grid host GameObject.
	///   Transforms are not driven by DOTS; the chunk GO is parented to the grid GO directly.
	/// </summary>
	[RequireMatchingQueriesForUpdate]
	[UpdateInGroup(typeof(SimulationSystemGroup))]
	[UpdateAfter(typeof(ManagedVoxelMeshingSystem))]
	public partial class AtlasHybridInstantiationSystem : SystemBase
	{
		protected override void OnUpdate()
		{
			using var _ = VoxelProfiler.Marks.AtlasHybridInstantiationSystem_Update.Auto();

			var ecb = SystemAPI.GetSingleton<EndSimST>().CreateCommandBuffer(World.Unmanaged);

			//TODO: fix chunksCreated limiter, it causes chunks to get stuck
			var toProcess = MeshingBudgets.Current.perFrame.chunksCreated;

			foreach (
				var (chunk, ltw, entity) in Query<AtlasedChunk, LocalToWorld>()
					.WithAll<ChunkNeedsHybridAllocation>()
					.WithEntityAccess()
			)
			{
				if (!VoxelEntityBridge.TryGetEntity(chunk.atlasId, out var atlasEntity))
					continue;
				if (!VoxelEntityBridge.TryGetAtlas(chunk.atlasId, out var atlas))
					continue;

				var prefabSettings = EntityManager.GetComponentObject<ChunkPrefabSettings>(atlasEntity);

				var rootGo = GetHostGameObject(chunk.atlasId);
				if (!rootGo || null == prefabSettings || !prefabSettings.prefab)
					continue;

				// Instantiate prefab and parent to grid GO
				var go = Object.Instantiate(prefabSettings.prefab, rootGo.transform);
				go.name = $"Chunk_{entity.Index}";
				go.hideFlags = HideFlags.NotEditable;

				var tr = go.transform;

				// var ltw = EntityManager.GetComponentData<LocalToWorld>(chunk);
				tr.position = ltw.Position;
				tr.rotation = ltw.Rotation;

				// Attach mesh filter and optional collider to the entity for downstream systems
				if (go.TryGetComponent(out MeshFilter mf))
					ecb.SetComponent(entity, new EntityMeshFilterAttachment { attachTo = mf });
				if (go.TryGetComponent(out MeshCollider mc))
					ecb.SetComponent(entity, new EntityMeshColliderAttachment { attachTo = mc });

				ecb.SetComponent(entity, new EntityFollowsGameObjectTransform { attachTo = tr });
				ecb.AddComponent(
					entity,
					new AtlasedChunkHybrid
					{
						//
						chunkGameObject = go,
						gridGameObject = rootGo,
					}
				);
				ecb.SetComponentEnabled<ChunkNeedsHybridAllocation>(entity, false);

				if (--toProcess <= 0)
					return;
			}
		}

		static GameObject GetHostGameObject(int instanceId)
		{
			return (GameObject)Resources.InstanceIDToObject(instanceId);
		}
	}
}
