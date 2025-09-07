namespace Voxels.Core.Hybrid.GameObjectLifecycle
{
	using Unity.Entities;

	public struct DestroyEntityByInstanceIDEvent : IComponentData
	{
		public int gameObjectInstanceID;
	}
}
