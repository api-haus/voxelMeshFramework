namespace Voxels.Core.Procedural
{
	using Unity.Entities;

	public class PopulateWithProceduralVoxelGenerator : IComponentData
	{
		public IProceduralVoxelGenerator generator;
	}
}
