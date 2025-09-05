namespace Voxels.Core.Grids
{
	using Unity.Entities;
	using Unity.Mathematics;

	public struct RollingGridCommitEvent : IComponentData, IEnableableComponent
	{
		public int gridID;
		public int3 targetAnchorWorldChunk;
		public float3 targetOriginWorld;
	}
}
