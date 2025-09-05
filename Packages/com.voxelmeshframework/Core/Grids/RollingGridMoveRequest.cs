namespace Voxels.Core.Grids
{
	using Unity.Entities;
	using Unity.Mathematics;

	public struct RollingGridMoveRequest : IComponentData, IEnableableComponent
	{
		public int3 targetAnchorWorldChunk;
	}
}
