namespace Voxels.Core.Grids
{
	using Unity.Entities;
	using Unity.Mathematics;

	public struct RollingGridConfig : IComponentData
	{
		public bool enabled;
		public int3 slotDims; // optional; if zero, systems compute from bounds
	}
}
