namespace Voxels.Core.Procedural.Generators
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

		public override JobHandle Schedule(
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

			// 2) Paint materials based on SDF and heightfield gradient
			inputDeps = new MaterialPaintJob
			{
				ltw = ltw,
				bounds = localBounds,
				voxelSize = voxelSize,
				volumeData = data,
				noiseFrequency = noiseFrequency,
				groundPlaneY = groundPlaneY,
				groundAmplitude = groundAmplitude,
				seed = seed,
				materialGround = materialGround,
				materialGold = materialGold,
				materialGrass = materialGrass,
				grassUpDotThreshold = grassUpDotThreshold,
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

		[BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
		struct MaterialPaintJob : IJobFor
		{
			public float voxelSize;
			public MinMaxAABB bounds;
			public VoxelVolumeData volumeData;
			public float4x4 ltw;

			public float noiseFrequency;
			public float groundPlaneY;
			public float groundAmplitude;
			public uint seed;
			public byte materialGround;
			public byte materialGold;
			public byte materialGrass;
			public float grassUpDotThreshold;
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
				// Convert stored quantized sdf back to world units for threshold decisions
				var sWorld = volumeData.sdfVolume[index] / sdfScale;

				// Approximate normal from heightfield derivatives for up/grass detection
				var seed2 = new float2(seed, seed);
				var eps = voxelSize;
				var hpx =
					groundPlaneY
					+ (snoise(((coord.xz + float2(eps, 0)) * noiseFrequency) + seed2) * groundAmplitude);
				var hmx =
					groundPlaneY
					+ (snoise(((coord.xz + float2(-eps, 0)) * noiseFrequency) + seed2) * groundAmplitude);
				var hpz =
					groundPlaneY
					+ (snoise(((coord.xz + float2(0, eps)) * noiseFrequency) + seed2) * groundAmplitude);
				var hmz =
					groundPlaneY
					+ (snoise(((coord.xz + float2(0, -eps)) * noiseFrequency) + seed2) * groundAmplitude);
				var dhdx = (hpx - hmx) / (2f * eps);
				var dhdz = (hpz - hmz) / (2f * eps);
				var grad = normalize(float3(-dhdx, 1f, -dhdz));

				// Outside (air): pad one-voxel shell with material to support blending
				if (sWorld < 0f)
				{
					if (abs(sWorld) <= voxelSize)
					{
						var padMat = grad.y > grassUpDotThreshold ? materialGrass : materialGround;
						volumeData.materials[index] = padMat;
					}
					else
					{
						volumeData.materials[index] = MATERIAL_AIR;
					}

					return;
				}

				// Inside: choose gold vs ground by secondary 2D noise
				var n2 = snoise((coord.xz * noiseFrequency * 2f) + (seed2 + 100f));
				var insideMat = n2 > 0.2f ? materialGold : materialGround;

				// Near-surface: check sign change in 6-neighborhood using SDF volume
				var nearAir = false;
				// X+1
				if (!nearAir && x < CHUNK_SIZE_MINUS_ONE)
				{
					var neighbor = (float)volumeData.sdfVolume[index + (1 << X_SHIFT)];
					if (neighbor < 0f)
						nearAir = true;
				}

				// X-1
				if (!nearAir && x > 0)
				{
					var neighbor = (float)volumeData.sdfVolume[index - (1 << X_SHIFT)];
					if (neighbor < 0f)
						nearAir = true;
				}

				// Y+1
				if (!nearAir && y < CHUNK_SIZE_MINUS_ONE)
				{
					var neighbor = (float)volumeData.sdfVolume[index + (1 << Y_SHIFT)];
					if (neighbor < 0f)
						nearAir = true;
				}

				// Y-1
				if (!nearAir && y > 0)
				{
					var neighbor = (float)volumeData.sdfVolume[index - (1 << Y_SHIFT)];
					if (neighbor < 0f)
						nearAir = true;
				}

				// Z+1
				if (!nearAir && z < CHUNK_SIZE_MINUS_ONE)
				{
					var neighbor = (float)volumeData.sdfVolume[index + 1];
					if (neighbor < 0f)
						nearAir = true;
				}

				// Z-1
				if (!nearAir && z > 0)
				{
					var neighbor = (float)volumeData.sdfVolume[index - 1];
					if (neighbor < 0f)
						nearAir = true;
				}

				var mat = insideMat;
				if (nearAir)
				{
					// Up-facing surfaces become grass
					if (grad.y > grassUpDotThreshold)
						mat = materialGrass;
					else
						mat = materialGround;
				}

				volumeData.materials[index] = mat;
			}
		}
	}
}
