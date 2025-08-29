namespace Voxels.Core.Hybrid
{
	using Unity.Entities;

	public struct DestroyEntityByInstanceIDEvent : IComponentData
	{
		public int gameObjectInstanceID;
	}
}
