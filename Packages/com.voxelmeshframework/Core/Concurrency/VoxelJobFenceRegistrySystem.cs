namespace Voxels.Core.Concurrency
{
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Jobs;

	public static class VoxelJobFenceRegistry
	{
		private class FencesKey { }

		private class RegistryContext { }

		static readonly SharedStatic<NativeParallelHashMap<Entity, JobHandle>> s_Fences = SharedStatic<
			NativeParallelHashMap<Entity, JobHandle>
		>.GetOrCreate<RegistryContext, FencesKey>();

		public static bool IsCreated => s_Fences.Data.IsCreated;

		public static void Initialize(int capacity = 16384)
		{
			if (s_Fences.Data.IsCreated)
				return;
			s_Fences.Data = new NativeParallelHashMap<Entity, JobHandle>(capacity, Allocator.Persistent);
		}

		public static void Dispose()
		{
			if (s_Fences.Data.IsCreated)
				s_Fences.Data.Dispose();
		}

		public static JobHandle Get(Entity e)
		{
			return s_Fences.Data.IsCreated && s_Fences.Data.TryGetValue(e, out var h) ? h : default;
		}

		public static JobHandle Tail(Entity e)
		{
			return Get(e);
		}

		public static void Update(Entity e, JobHandle handle)
		{
			if (!s_Fences.Data.IsCreated)
				return;
			s_Fences.Data.Remove(e);
			s_Fences.Data.TryAdd(e, handle);
		}

		public static void Reset(Entity e)
		{
			if (s_Fences.Data.IsCreated)
				s_Fences.Data.Remove(e);
		}

		public static void CompleteAndReset(Entity e)
		{
			var h = Get(e);
			h.Complete();
			Reset(e);
		}

		public static bool TryComplete(Entity e)
		{
			if (!s_Fences.Data.IsCreated)
				return true;
			if (!s_Fences.Data.TryGetValue(e, out var h))
				return true;
			if (h.Equals(default(JobHandle)) || h.IsCompleted)
			{
				// Complete the fence, to ensure all workloads are complete. This is required, since Unity made it this way.
				h.Complete();
				// Fence is effectively complete; clear entry
				s_Fences.Data.Remove(e);
				return true;
			}
			return false;
		}
	}

	[WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
	[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
	public partial struct VoxelJobFenceRegistrySystem : ISystem
	{
		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			VoxelJobFenceRegistry.Initialize();
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state)
		{
			VoxelJobFenceRegistry.Dispose();
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state) { }
	}
}
