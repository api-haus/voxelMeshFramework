namespace Voxels.Core.Atlasing.Components
{
	using System.Runtime.InteropServices;
	using Unity.Collections;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Jobs;

	[StructLayout(LayoutKind.Sequential)]
	public struct ChunkAtlasCounters : INativeDisposable
	{
		public NativeReference<int> pendingChunks;
		public NativeReference<int> totalChunks;

		public ChunkAtlasCounters(Allocator persistent)
		{
			//
			pendingChunks = new(0, persistent);
			totalChunks = new(0, persistent);
		}

		public readonly unsafe UnsafeAtomicCounter32 Total()
		{
			return new UnsafeAtomicCounter32(totalChunks.GetUnsafePtr());
		}

		public readonly unsafe UnsafeAtomicCounter32 Pending()
		{
			return new UnsafeAtomicCounter32(pendingChunks.GetUnsafePtr());
		}

		public void Dispose()
		{
			pendingChunks.Dispose();
			totalChunks.Dispose();
		}

		public JobHandle Dispose(JobHandle inputDeps)
		{
			inputDeps = pendingChunks.Dispose(inputDeps);
			inputDeps = totalChunks.Dispose(inputDeps);
			return inputDeps;
		}
	}
}
