namespace Voxels.Core.Grids
{
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using static VoxelConstants;

	public struct NativeVoxelObject : IComponentData
	{
		public float voxelSize;

		public readonly MinMaxAABB Bounds(float3 position)
		{
			var size = CHUNK_SIZE * voxelSize;
			var center = position + (size * .5f);
			var bounds = MinMaxAABB.CreateFromCenterAndExtents(center, size);

			return bounds;
		}
	}
}
