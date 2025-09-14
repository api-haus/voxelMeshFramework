namespace Voxels.Core.Procedural.Generators.VoxelSDF
{
	using Spatial;
	using Unity.Burst;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using UnityEngine;
	using static Unity.Mathematics.math;
	using static VoxelConstants;

	public sealed class HalfSphereVoxelGenerator : ProceduralVoxelGeneratorBehaviour
	{
		[SerializeField]
		float radius = 20f;

		[SerializeField]
		Vector3 center = Vector3.zero;

		[SerializeField]
		uint seed = 1;

		[SerializeField]
		float noiseFrequency = 0.1f;

		[SerializeField]
		byte materialGround = 1;

		[SerializeField]
		byte materialGold = 2;

		[SerializeField]
		byte materialGrass = 3;

		[SerializeField]
		float grassUpDotThreshold = 0.7f;

		[SerializeField]
		[Range(1, 64)]
		int sdfSamplesPerVoxel = 16;

		public override JobHandle ScheduleVoxels(
			MinMaxAABB localBounds,
			float4x4 ltw,
			float voxelSize,
			VoxelVolumeData data,
			JobHandle inputDeps
		)
		{
			// Linear quantization scale to pack more resolution per voxel into sbyte
			var sdfScale = sdfSamplesPerVoxel / voxelSize;
			// 1) Generate hemisphere SDF (inside-positive). Hemisphere is sphere intersected with half-space y >= center.y (Y+ oriented)
			inputDeps = new SdfJob
			{
				ltw = ltw,
				bounds = localBounds,
				voxelSize = voxelSize,
				volumeData = data,
				centerWorld = center,
				radius = radius,
				sdfScale = sdfScale,
			}.Schedule(VOLUME_LENGTH, inputDeps);

			return inputDeps;
		}

		[BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
		struct SdfJob : IJobFor
		{
			public float voxelSize;
			public MinMaxAABB bounds;
			public VoxelVolumeData volumeData;

			public float4x4 ltw;
			public float3 centerWorld;
			public float radius;
			public float sdfScale;

			public void Execute(int index)
			{
				// Reverse of linear index mapping: index = (x << X_SHIFT) + (y << Y_SHIFT) + z
				var x = (index >> X_SHIFT) & CHUNK_SIZE_MINUS_ONE;
				var y = (index >> Y_SHIFT) & CHUNK_SIZE_MINUS_ONE;
				var z = index & CHUNK_SIZE_MINUS_ONE;

				float3 localCoord = int3(x, y, z);
				var coord = bounds.Min + (localCoord * voxelSize);

				coord = transform(ltw, coord);

				var toCenter = coord - centerWorld;
				var d = length(toCenter);
				var sSphere = radius - d; // inside-positive sphere SDF
				var sPlane = centerWorld.y - coord.y; // inside-positive half-space (y <= center.y)
				var sHemisphere = min(sSphere, sPlane); // intersection for inside-positive SDFs

				var sdfByte = clamp(sHemisphere * sdfScale, -127f, 127f);
				volumeData.sdfVolume[index] = (sbyte)sdfByte;
			}
		}
	}
}
