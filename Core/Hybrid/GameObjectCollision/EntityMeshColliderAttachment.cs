namespace Voxels.Core.Hybrid.GameObjectCollision
{
	using Unity.Entities;
	using UnityEngine;

	public class EntityMeshColliderAttachment : IComponentData
	{
		public MeshCollider attachTo;
	}
}
