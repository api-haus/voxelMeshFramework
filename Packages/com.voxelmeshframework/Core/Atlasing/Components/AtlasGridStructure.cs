namespace Voxels.Core.Atlasing.Components
{
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;

	public struct AtlasGridStructure : ICleanupComponentData
	{
		public NativeParallelHashMap<int3, Entity> coordinateMapping;
	}
}
