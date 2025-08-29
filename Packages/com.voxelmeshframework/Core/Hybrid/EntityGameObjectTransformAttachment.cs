namespace Voxels.Core.Hybrid
{
	using Unity.Entities;
	using UnityEngine;

	public class EntityGameObjectTransformAttachment : IComponentData
	{
		public Transform attachTo;
	}
}
