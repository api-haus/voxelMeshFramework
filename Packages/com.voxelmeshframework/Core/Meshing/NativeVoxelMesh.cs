namespace Voxels.Core.Meshing
{
	using Spatial;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Jobs;

	public struct NativeVoxelMesh : ICleanupComponentData, INativeDisposable
	{
		public struct CleanupTag : IComponentData { }

		public VoxelVolumeData volume;
		public NativeVoxelMeshing meshing;

		public NativeVoxelMesh(Allocator allocator = Allocator.Persistent)
		{
			volume = new(allocator);
			meshing = new(allocator);
		}

		public void Dispose()
		{
			volume.Dispose();
			meshing.Dispose();
		}

		public JobHandle Dispose(JobHandle inputDeps)
		{
			inputDeps = meshing.Dispose(inputDeps);
			inputDeps = volume.Dispose(inputDeps);
			return inputDeps;
		}
	}
}
