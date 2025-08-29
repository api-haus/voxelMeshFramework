namespace Voxels.Core.Hybrid
{
	using Unity.Entities;
	using UnityEngine;

	public class EntityMeshColliderAttachment : IComponentData
	{
		public MeshCollider attachTo;
	}
}
