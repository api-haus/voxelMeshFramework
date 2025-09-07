namespace Voxels.Core.Hybrid.GameObjectRendering
{
	using Unity.Entities;
	using UnityEngine;

	public class EntityMeshFilterAttachment : IComponentData
	{
		public MeshFilter attachTo;
	}
}
