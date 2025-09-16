namespace Voxels.Core.Hybrid
{
	using Atlasing.Components;
	using Authoring;
	using GameObjectLifecycle;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Logging;
	using static Diagnostics.VoxelProfiler.Marks;
	using Entity = Unity.Entities.Entity;

	public static class VoxelEntityBridge
	{
		public static T GetSingleton<T>()
			where T : unmanaged, IComponentData
		{
			if (!TryGetEntityManager(out var em))
				return default;
			var mapQuery = em.CreateEntityQuery(ComponentType.ReadOnly<T>());
			if (!mapQuery.IsEmptyIgnoreFilter)
			{
				var st = mapQuery.GetSingleton<T>();
				return st;
			}

			return default;
		}

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
