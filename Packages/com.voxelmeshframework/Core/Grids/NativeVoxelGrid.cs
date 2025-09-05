namespace Voxels.Core.Grids
{
	using Unity.Entities;
	using Unity.Mathematics.Geometry;

	public struct NativeVoxelGrid : IComponentData
	{
		public int gridID;

		public float voxelSize;

		public MinMaxAABB bounds;

		public struct MeshingBudget : IComponentData
		{
			public int maxMeshesPerFrame;
		}

		public struct FullyMeshedEvent : IComponentData, IEnableableComponent { }
	}
}
