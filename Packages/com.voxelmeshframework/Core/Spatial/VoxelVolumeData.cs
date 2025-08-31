namespace Voxels.Core.Spatial
{
	using Unity.Collections;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Jobs;
	using static VoxelConstants;

	public struct VoxelVolumeData : INativeDisposable
	{
		/// <summary>
		///   VOXEL DATA STORAGE: 32³ Signed Distance Field Values
		///   This NativeArray stores the core voxel data for the chunk:
		///   DATA CHARACTERISTICS:
		///   - Size: 32³ = 32,768 voxels
		///   - Type: sbyte (signed 8-bit integer)
		///   - Range: -127 (solid interior) to +127 (air exterior)
		///   - Zero crossing: Surface boundary location
		///   MEMORY ALLOCATION:
		///   - Allocator.Persistent: Long-lived allocation
		///   - No garbage collection pressure
		///   - Thread-safe access for job system
		///   - Stable memory address for SIMD processing
		///   ACCESS PATTERN:
		///   - Use bit shift constants for fast indexing
		///   - Linear memory layout optimizes cache performance
		///   - SIMD-friendly alignment for vectorized operations
		///   This is the foundation data that the Surface Nets algorithm processes
		///   to extract smooth, seamless isosurfaces for rendering.
		/// </summary>
		[NativeDisableContainerSafetyRestriction]
		public NativeArray<sbyte> sdfVolume;

		[NativeDisableContainerSafetyRestriction]
		public NativeArray<byte> materials;

		public float voxelSize;

		public VoxelVolumeData(Allocator allocator = Allocator.Persistent)
		{
			const int volumeSize = CHUNK_SIZE * CHUNK_SIZE * CHUNK_SIZE;

			sdfVolume = new(volumeSize, allocator);
			materials = new(volumeSize, allocator);
			voxelSize = 1;

			unsafe
			{
				UnsafeUtility.MemSet(
					sdfVolume.GetUnsafePtr(),
					unchecked((byte)sbyte.MinValue),
					UnsafeUtility.SizeOf<sbyte>() * volumeSize
				);
			}
		}

		public VoxelVolumeData(UnsafeVoxelData unsafeVoxelData, Allocator allocator)
		{
			sdfVolume = unsafeVoxelData.GetSDF(allocator);
			materials = unsafeVoxelData.GetMat(allocator);
			voxelSize = 1;
		}

		public void Dispose()
		{
			sdfVolume.Dispose();
			materials.Dispose();
		}

		public JobHandle Dispose(JobHandle inputDeps)
		{
			inputDeps = sdfVolume.Dispose(inputDeps);
			inputDeps = materials.Dispose(inputDeps);
			return inputDeps;
		}
	}
}
