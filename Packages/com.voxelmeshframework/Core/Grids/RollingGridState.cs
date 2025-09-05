namespace Voxels.Core.Grids
{
	using Unity.Entities;
	using Unity.Mathematics;

	public struct RollingGridState : IComponentData
	{
		public int3 anchorWorldChunk;
		public int3 dims;
	}
}
