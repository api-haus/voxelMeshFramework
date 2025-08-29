namespace Voxels.Core.Spatial
{
	using Meshing;
	using Unity.Collections;
	using static Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility;
	using static VoxelConstants;

	public readonly unsafe struct UnsafeVoxelData
	{
		readonly void* m_SDFPtr;
		readonly void* m_MatPtr;

		internal NativeArray<sbyte> GetSDF(Allocator allocator = Allocator.None)
		{
			return ConvertExistingDataToNativeArray<sbyte>(m_SDFPtr, VOLUME_LENGTH, allocator);
		}

		internal NativeArray<byte> GetMat(Allocator allocator = Allocator.None)
		{
			return ConvertExistingDataToNativeArray<byte>(m_MatPtr, VOLUME_LENGTH, allocator);
		}

		public UnsafeVoxelData(NativeVoxelMesh vm)
			: this(vm.volume) { }

		public UnsafeVoxelData(VoxelVolumeData vd)
		{
			m_SDFPtr = vd.sdfVolume.GetUnsafePtr();
			m_MatPtr = vd.materials.GetUnsafePtr();
		}
	}
}
