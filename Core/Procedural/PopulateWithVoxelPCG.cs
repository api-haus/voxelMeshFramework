namespace Voxels.Core.Procedural
{
	using Generators;
	using Unity.Entities;

	public class PopulateWithProceduralVoxelGenerator : IComponentData
	{
		public IProceduralVoxelGenerator generator;
	}
}
