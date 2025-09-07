namespace Voxels.Core.Budgets
{
	using Unity.Entities;

	public struct VoxelBudgetsChangeRequest : IComponentData
	{
		public VoxelBudgets newBudgets;
	}
}
