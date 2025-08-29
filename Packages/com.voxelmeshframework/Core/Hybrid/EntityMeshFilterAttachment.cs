namespace Voxels.Core.Hybrid
{
	using Unity.Entities;
	using UnityEngine;

	public class EntityMeshFilterAttachment : IComponentData
	{
		public MeshFilter attachTo;
	}
}
