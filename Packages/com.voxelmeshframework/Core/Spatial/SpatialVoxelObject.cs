namespace Voxels.Core.Spatial
{
	using Unity.Entities;
	using Unity.Mathematics.Geometry;

	public struct SpatialVoxelObject
	{
		public MinMaxAABB bounds;
		public UnsafeVoxelData voxelData;
		public Entity entity;
		public float voxelSize;
	}
}
