namespace Voxels.Core.Hybrid
{
	using System;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using static Unity.Entities.SystemAPI;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;

	public struct EntityByInstanceID : IComponentData, IDisposable
	{
		public NativeParallelHashMap<int, Entity> map;

		public void Dispose()
		{
			map.Dispose();
		}
	}

	[RequireMatchingQueriesForUpdate]
	public partial struct EntityInstanceIDLifecycleSystem : ISystem
	{
		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			const int objectCapacity = 16384;

			state.EntityManager.CreateSingleton(
				new EntityByInstanceID
				{
					map = new NativeParallelHashMap<int, Entity>(objectCapacity, Allocator.Persistent),
				}
			);
			state.RequireForUpdate<EndSimST>();
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state)
		{
			GetSingleton<EntityByInstanceID>().Dispose();
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			var ecb = GetSingleton<EndSimST>().CreateCommandBuffer(state.WorldUnmanaged);
			ref var st = ref GetSingletonRW<EntityByInstanceID>().ValueRW;

			foreach (
				var (at, entity) in Query<RefRO<EntityGameObjectInstanceIDAttachment>>()
					.WithEntityAccess()
					.WithChangeFilter<EntityGameObjectInstanceIDAttachment>()
			)
			{
				var gid = at.ValueRO.gameObjectInstanceID;
				st.map.Remove(gid);
				st.map.Add(gid, entity);
			}

			foreach (
				var (evt, entity) in Query<RefRO<DestroyEntityByInstanceIDEvent>>().WithEntityAccess()
			)
				if (st.map.TryGetValue(evt.ValueRO.gameObjectInstanceID, out var associatedEntity))
				{
					// TODO: clear event after some time if we cannot find the entity, perchance
					ecb.DestroyEntity(associatedEntity);
					ecb.DestroyEntity(entity);
				}
		}
	}
}
