namespace Voxels.Core.Procedural
{
	using Spatial;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using UnityEngine;

	public abstract class ProceduralMaterialGeneratorBehaviour
		: MonoBehaviour,
			IProceduralMaterialGenerator
	{
		public abstract JobHandle ScheduleMaterials(
			MinMaxAABB localBounds,
			float4x4 ltw,
			float voxelSize,
			VoxelVolumeData data,
			JobHandle inputDeps
		);
	}
}
