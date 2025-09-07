namespace Voxels.Core.Hybrid.GameObjectTransforms
{
	using Unity.Entities;
	using UnityEngine;

	public class EntityFollowsGameObjectTransform : IComponentData
	{
		public Transform attachTo;
	}
}
