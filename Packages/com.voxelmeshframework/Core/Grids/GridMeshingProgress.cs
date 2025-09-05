namespace Voxels.Core.Grids
{
	using Unity.Entities;

	public struct GridMeshingProgress : IComponentData
	{
		public int totalChunks;
		public int allocatedChunks;
		public int processedCount;
		public int meshedOnceCount;
		public bool firedOnce;
	}
}
