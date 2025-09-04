namespace Voxels.Core.Grids
{
	using Meshing.Tags;
	using Procedural.Tags;
	using Unity.Burst;
	using Unity.Entities;
	using static Unity.Entities.SystemAPI;
	using EndInitST = Unity.Entities.EndInitializationEntityCommandBufferSystem.Singleton;

	/// <summary>
	///   Mirrors grid-level Needs* tags to all owned chunks via LinkedEntityGroup, then consumes the grid tag.
	/// </summary>
	[UpdateInGroup(typeof(InitializationSystemGroup))]
	[UpdateAfter(typeof(GridChunkAllocationSystem))]
	public partial struct GridNeedsTagPropagationSystem : ISystem
	{
		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			state.RequireForUpdate<EndInitST>();
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			var ecb = GetSingleton<EndInitST>().CreateCommandBuffer(state.WorldUnmanaged);

			foreach (
				var (leg, entity) in Query<DynamicBuffer<LinkedEntityGroup>>()
					.WithAll<NativeVoxelGrid>()
					.WithEntityAccess()
			)
			{
				var remesh = IsComponentEnabled<NeedsRemesh>(entity);
				var spatial = IsComponentEnabled<NeedsSpatialUpdate>(entity);
				var procedural = IsComponentEnabled<NeedsProceduralUpdate>(entity);

				if (!remesh && !procedural && !spatial)
					continue;

				for (var i = 1; i < leg.Length; i++)
				{
					var child = leg[i].Value;
					if (remesh)
						ecb.SetComponentEnabled<NeedsRemesh>(child, true);
					if (spatial)
						ecb.SetComponentEnabled<NeedsSpatialUpdate>(child, true);
					if (procedural)
						ecb.SetComponentEnabled<NeedsProceduralUpdate>(child, true);
				}

				// Turn off from greed - we propagated.
				if (remesh)
					ecb.SetComponentEnabled<NeedsRemesh>(entity, false);
				if (spatial)
					ecb.SetComponentEnabled<NeedsSpatialUpdate>(entity, false);
				if (procedural)
					ecb.SetComponentEnabled<NeedsProceduralUpdate>(entity, false);
			}
		}
	}
}
