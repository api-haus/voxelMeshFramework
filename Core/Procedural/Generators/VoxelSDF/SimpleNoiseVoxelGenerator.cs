namespace Voxels.Core.Procedural.Generators.VoxelSDF
{
	using Spatial;
	using Unity.Burst;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using UnityEngine;
	using UnityEngine.Serialization;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Mathematics.math;
	using static Unity.Mathematics.noise;
	using static VoxelConstants;

	public sealed class SimpleNoiseVoxelGenerator : ProceduralVoxelGeneratorBehaviour
	{
		[SerializeField]
		[FormerlySerializedAs("scale")]
		float noiseFrequency = 0.1f;

		[SerializeField]
		float groundPlaneY;

		[SerializeField]
		float groundAmplitude = 10f;

		[SerializeField]
		uint seed = 1;

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
			using var _ = SimpleNoiseVoxelGenerator_Schedule.Auto();

			// Choose a linear quantization scale to pack more resolution per voxel into sbyte
			// StoredSdf = clamp((worldDistance * sdfScale), -127..127)
			// where sdfScale = sdfSamplesPerVoxel / voxelSize
			var sdfScale = sdfSamplesPerVoxel / voxelSize;

			// 1) Generate SDF
			inputDeps = new SdfJob
			{
				ltw = ltw,
				bounds = localBounds,
				voxelSize = voxelSize,
				volumeData = data,
				noiseFrequency = noiseFrequency,
				groundPlaneY = groundPlaneY,
				groundAmplitude = groundAmplitude,
				seed = seed,
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

			public float noiseFrequency;
			public float groundPlaneY;
			public float groundAmplitude;
			public uint seed;
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

				// Heightfield terrain: height = groundPlaneY + noise2D(xz) * groundAmplitude
				var seed2 = new float2(seed, seed);
				var heightNoise = snoise((coord.xz * noiseFrequency) + seed2);
				var surfaceHeight = groundPlaneY + (heightNoise * groundAmplitude);

				// Signed distance in world units (positive below/inside the surface)
				var sdfWorld = surfaceHeight - coord.y;
				// Quantize to sbyte with increased resolution per voxel
				var sdfByte = clamp(sdfWorld * sdfScale, -127f, 127f);

				volumeData.sdfVolume[index] = (sbyte)sdfByte;
			}
		}
	}
}
