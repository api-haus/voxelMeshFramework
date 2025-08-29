namespace Voxels.Core.Grids
{
	using Unity.Entities;
	using Unity.Mathematics.Geometry;

	public struct NativeVoxelGrid : IComponentData
	{
		public int gridID;

		public float voxelSize;

		public MinMaxAABB bounds;
	}
}
