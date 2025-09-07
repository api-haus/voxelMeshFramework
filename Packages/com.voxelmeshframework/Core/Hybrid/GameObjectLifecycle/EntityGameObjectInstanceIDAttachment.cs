namespace Voxels.Core.Hybrid.GameObjectLifecycle
{
	using Unity.Entities;

	public struct EntityGameObjectInstanceIDAttachment : IComponentData
	{
		public int gameObjectInstanceID;
	}
}
