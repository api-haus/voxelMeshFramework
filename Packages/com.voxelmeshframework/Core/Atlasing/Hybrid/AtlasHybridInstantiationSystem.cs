namespace Voxels.Core.Atlasing.Hybrid
{
	using Budgets;
	using Components;
	using Core.Hybrid.GameObjectCollision;
	using Core.Hybrid.GameObjectLifecycle;
	using Core.Hybrid.GameObjectRendering;
	using Core.Hybrid.GameObjectTransforms;
	using Diagnostics;
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
			var toProcess = VoxelBudgets.Current.perFrame.chunksCreated;

			// TODO: only instantiate by event instead of every-frame reiteration

			// For each grid with prefab settings and instance id, instantiate missing chunk GOs
			foreach (
				var (prefabSettings, goAttachment, leg, atlas, gridEntity) in Query<
					ChunkPrefabSettings,
					EntityGameObjectInstanceIDAttachment,
					DynamicBuffer<LinkedEntityGroup>,
					NativeChunkAtlas
				>()
					.WithEntityAccess()
			)
			{
				var rootGo = GetHostGameObject(goAttachment.gameObjectInstanceID);
				if (!rootGo || !prefabSettings.prefab)
					continue;

				for (var i = 1; i < leg.Length; i++)
				{
					var chunk = leg[i].Value;
					if (SystemAPI.HasComponent<AtlasedChunkHybrid>(chunk))
						continue;
					if (!IsComponentEnabled<ChunkNeedsHybridAllocation>(chunk))
						continue;

					// Instantiate prefab and parent to grid GO
					var go = Object.Instantiate(prefabSettings.prefab, rootGo.transform);
					go.name = $"Chunk_{chunk.Index}";
					go.hideFlags = HideFlags.NotEditable;

					var tr = go.transform;

					var ltw = EntityManager.GetComponentData<LocalToWorld>(chunk);
					tr.position = ltw.Position;
					tr.rotation = ltw.Rotation;

					// Attach mesh filter and optional collider to the entity for downstream systems
					if (go.TryGetComponent(out MeshFilter mf))
						ecb.SetComponent(chunk, new EntityMeshFilterAttachment { attachTo = mf });
					if (go.TryGetComponent(out MeshCollider mc))
						ecb.SetComponent(chunk, new EntityMeshColliderAttachment { attachTo = mc });

					ecb.SetComponent(chunk, new EntityFollowsGameObjectTransform { attachTo = tr });
					ecb.AddComponent(
						chunk,
						new AtlasedChunkHybrid
						{
							//
							chunkGameObject = go,
							gridGameObject = rootGo,
						}
					);
					ecb.SetComponentEnabled<ChunkNeedsHybridAllocation>(chunk, false);

					if (--toProcess <= 0)
						return;
				}
			}
		}

		static GameObject GetHostGameObject(int instanceId)
		{
			return (GameObject)Resources.InstanceIDToObject(instanceId);
		}
	}
}
