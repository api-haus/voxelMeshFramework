namespace Voxels.Core.Procedural
{
	using Unity.Entities;

	public class PopulateWithProceduralVoxelGenerator : IComponentData
	{
		public IProceduralMaterialGenerator materials;
		public IProceduralVoxelGenerator voxels;
	}
}
