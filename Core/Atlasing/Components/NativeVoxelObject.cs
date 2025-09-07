namespace Voxels.Core.Atlasing.Components
{
	using Unity.Entities;
	using Unity.Mathematics.Geometry;

	public struct NativeVoxelObject : IComponentData
	{
		public float voxelSize;
		public MinMaxAABB localBounds;
	}
}
