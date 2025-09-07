namespace Voxels.Core.Atlasing.Components
{
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Jobs;
	using Unity.Mathematics.Geometry;

	public struct NativeChunkAtlas : ICleanupComponentData, INativeDisposable
	{
		public struct CleanupTag : IComponentData { }

		public int atlasId;
		public bool editable;
		public float voxelSize;
		public MinMaxAABB bounds;
		public ChunkAtlasCounters counters;

		public void Dispose()
		{
			counters.Dispose();
		}

		public JobHandle Dispose(JobHandle inputDeps)
		{
			inputDeps = counters.Dispose(inputDeps);
			return inputDeps;
		}
	}
}
