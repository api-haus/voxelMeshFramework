namespace Voxels.Core.Concurrency
{
	using System;
	using Meshing.Budgets;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Jobs;
	using UnityEngine;

	public static class VoxelJobFenceRegistry
	{
		static readonly SharedStatic<FenceRegistry> s_fences =
			SharedStatic<FenceRegistry>.GetOrCreate<FenceRegistry>();

		static ref FenceRegistry Registry => ref s_fences.Data;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void EnsureInitialized()
		{
			if (!Registry.IsCreated)
				Initialize();
		}

		public static void Initialize(int capacity = 16384)
		{
			if (Registry.IsCreated)
				return;
			Registry = new(capacity, Allocator.Persistent);
		}

		public static void Release()
		{
			Registry.Dispose();
		}

		public static JobHandle GetFence(Entity e)
		{
			return Registry.entityJobs.TryGetValue(e, out var h) ? h : default;
		}

		public static JobHandle Tail(Entity e)
		{
			return GetFence(e);
		}

		public static void UpdateFence(Entity e, JobHandle handle)
		{
			Registry.entityJobs.Remove(e);
			Registry.entityJobs.TryAdd(e, handle);
		}

		public static void Reset(Entity e)
		{
			EnsureInitialized();
			Registry.entityJobs.Remove(e);
		}

		public static void CompleteAndReset(Entity e)
		{
			var h = GetFence(e);
			h.Complete();
			Reset(e);
		}

		public static bool TryComplete(Entity e)
		{
			if (!Registry.entityJobs.TryGetValue(e, out var h))
				return true;
			var isAsync = MeshingBudgets.Current.async;
			if (h.Equals(default) || h.IsCompleted || !isAsync)
			{
				// Complete the fence, to ensure all workloads are complete. This is required, since Unity made it this way.
				h.Complete();
				// Fence is effectively complete; clear entry
				Registry.entityJobs.Remove(e);
				return true;
			}

			return false;
		}

		struct FenceRegistry : IDisposable
		{
			public NativeParallelHashMap<Entity, JobHandle> entityJobs;

			public FenceRegistry(int capacity, Allocator allocator) =>
				entityJobs = new(capacity, allocator);

			public bool IsCreated => entityJobs.IsCreated;

			public void Dispose()
			{
				entityJobs.Dispose();
			}
		}
	}
}
