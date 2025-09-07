namespace Voxels.Core.ThirdParty.SurfaceNets.Utils
{
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Jobs;
	using static Unity.Collections.LowLevel.Unsafe.UnsafeUtility;

	/// <summary>
	///   Because NativeReference.value is not ref
	/// </summary>
	public unsafe struct UnsafePointer<T> : INativeDisposable
		where T : unmanaged
	{
		[NoAlias]
		[NativeDisableUnsafePtrRestriction]
		public T* pointer;

		internal Allocator allocator;

		public UnsafePointer(T defaultValue, Allocator allocator = Allocator.Persistent)
		{
			this.allocator = allocator;
			pointer = (T*)Malloc(SizeOf<T>(), AlignOf<T>(), allocator);
			*pointer = defaultValue;
		}

		public static UnsafePointer<T> Create()
		{
			return new UnsafePointer<T>(default);
		}

		public bool IsCreated => pointer != null;

		public ref T Item => ref *pointer;

		public void Dispose()
		{
			if (IsCreated)
				Free(pointer, allocator);
			pointer = null;
		}

		public JobHandle Dispose(JobHandle inputDeps)
		{
			if (IsCreated)
				inputDeps = new DisposePointerJob
				{
					//
					ptr = pointer,
					allocator = allocator,
				}.Schedule(inputDeps);
			pointer = null;

			return inputDeps;
		}
	}

	[BurstCompile]
	unsafe struct DisposePointerJob : IJob
	{
		[NativeDisableUnsafePtrRestriction]
		public void* ptr;

		public Allocator allocator;

		public void Execute()
		{
			Free(ptr, allocator);
		}
	}
}
