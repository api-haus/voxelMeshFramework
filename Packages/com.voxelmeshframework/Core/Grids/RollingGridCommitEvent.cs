namespace Voxels.Core.Grids
{
	using Unity.Entities;

	public struct RollingGridCommitEvent : IComponentData, IEnableableComponent
	{
		public int gridID;
	}
}
