namespace Voxels.Core.Spatial
{
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;

	public struct SpatialVoxelObject
	{
		public MinMaxAABB localBounds;
		public UnsafeVoxelData voxelData;
		public Entity entity;
		public float voxelSize;
		public float4x4 ltw;
		public float4x4 wtl;
	}
}
