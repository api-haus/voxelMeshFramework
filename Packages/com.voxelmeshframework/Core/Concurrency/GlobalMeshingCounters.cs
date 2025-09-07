namespace Voxels.Core.Concurrency
{
	using System;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Collections.LowLevel.Unsafe;
	using UnityEngine;

	public struct SharedCounters : IDisposable
	{
		public NativeReference<int> allocated;
		public NativeReference<int> meshed;
		public NativeReference<int> generated;
		public NativeReference<int> applied;

		public SharedCounters(Allocator allocator)
		{
			allocated = new(0, allocator);
			meshed = new(0, allocator);
			generated = new(0, allocator);
			applied = new(0, allocator);
		}

		public bool IsCreated => allocated.IsCreated;

		public void Dispose()
		{
			allocated.Dispose();
			meshed.Dispose();
			generated.Dispose();
			applied.Dispose();
		}

		static readonly SharedStatic<SharedCounters> s_shared =
			SharedStatic<SharedCounters>.GetOrCreate<SharedCounters>();

		internal static ref SharedCounters Ref => ref s_shared.Data;

		internal static unsafe UnsafeAtomicCounter32 Allocated()
		{
			return new UnsafeAtomicCounter32(Ref.allocated.GetUnsafePtr());
		}

		internal static unsafe UnsafeAtomicCounter32 Meshed()
		{
			return new UnsafeAtomicCounter32(Ref.meshed.GetUnsafePtr());
		}

		internal static unsafe UnsafeAtomicCounter32 Generated()
		{
			return new UnsafeAtomicCounter32(Ref.generated.GetUnsafePtr());
		}

		internal static unsafe UnsafeAtomicCounter32 Applied()
		{
			return new UnsafeAtomicCounter32(Ref.applied.GetUnsafePtr());
		}
	}

	public static class GlobalMeshingCounters
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
		static void InitializeOnLoad()
		{
			EnsureInitialized();
		}

		public static void EnsureInitialized()
		{
			if (!SharedCounters.Ref.IsCreated)
				SharedCounters.Ref = new(Allocator.Persistent);
		}

		public static void Release()
		{
			SharedCounters.Ref.Dispose();
			SharedCounters.Ref = default;
		}
	}
}
