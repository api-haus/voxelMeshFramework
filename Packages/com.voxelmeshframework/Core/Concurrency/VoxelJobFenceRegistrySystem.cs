namespace Voxels.Core.Concurrency
{
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Jobs;

	static class VoxelJobFenceRegistry
	{
		static readonly SharedStatic<NativeParallelHashMap<Entity, JobHandle>> s_fences = SharedStatic<
			NativeParallelHashMap<Entity, JobHandle>
		>.GetOrCreate<FencesKey>();

		public static void Initialize(int capacity = 16384)
		{
			if (s_fences.Data.IsCreated)
				return;
			s_fences.Data = new NativeParallelHashMap<Entity, JobHandle>(capacity, Allocator.Persistent);
		}

		public static void Dispose()
		{
			if (s_fences.Data.IsCreated)
				s_fences.Data.Dispose();
		}

		public static JobHandle GetFence(Entity e)
		{
			return s_fences.Data.IsCreated && s_fences.Data.TryGetValue(e, out var h) ? h : default;
		}

		public static JobHandle Tail(Entity e)
		{
			return GetFence(e);
		}

		public static void UpdateFence(Entity e, JobHandle handle)
		{
			if (!s_fences.Data.IsCreated)
				return;
			s_fences.Data.Remove(e);
			s_fences.Data.TryAdd(e, handle);
		}

		public static void Reset(Entity e)
		{
			if (s_fences.Data.IsCreated)
				s_fences.Data.Remove(e);
		}

		public static void CompleteAndReset(Entity e)
		{
			var h = GetFence(e);
			h.Complete();
			Reset(e);
		}

		public static bool TryComplete(Entity e)
		{
			if (!s_fences.Data.IsCreated)
				return true;
			if (!s_fences.Data.TryGetValue(e, out var h))
				return true;
			if (h.Equals(default) || h.IsCompleted)
			{
				// Complete the fence, to ensure all workloads are complete. This is required, since Unity made it this way.
				h.Complete();
				// Fence is effectively complete; clear entry
				s_fences.Data.Remove(e);
				return true;
			}

			return false;
		}

		class FencesKey { }
	}

	[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
	public partial struct VoxelJobFenceRegistrySystem : ISystem
	{
		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			// Keep as a safe fallback; bootstrap system should initialize with configured capacity
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
