namespace Voxels.Core.Meshing.Budgets
{
	using Unity.Entities;

	public struct MeshingBudgetsChangeRequest : IComponentData
	{
		public MeshingBudgets newBudgets;
	}
}
