namespace Voxels.Core.Procedural
{
	using Spatial;
	using Unity.Burst;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using UnityEngine;
	using static Unity.Mathematics.math;
	using static Unity.Mathematics.noise;
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

		public override JobHandle Schedule(
			MinMaxAABB localBounds,
			float4x4 ltw,
			float voxelSize,
			VoxelVolumeData data,
			JobHandle inputDeps
		)
		{
			// 1) Generate hemisphere SDF (inside-positive). Hemisphere is sphere intersected with half-space y >= center.y (Y+ oriented)
			inputDeps = new SdfJob
			{
				ltw = ltw,
				bounds = localBounds,
				voxelSize = voxelSize,
				volumeData = data,
				centerWorld = center,
				radius = radius,
			}.Schedule(VOLUME_LENGTH, inputDeps);

			// 2) Paint materials similar to SimpleNoiseVoxelGenerator
			inputDeps = new MaterialPaintJob
			{
				ltw = ltw,
				bounds = localBounds,
				voxelSize = voxelSize,
				volumeData = data,
				centerWorld = center,
				radius = radius,
				seed = seed,
				noiseFrequency = noiseFrequency,
				materialGround = materialGround,
				materialGold = materialGold,
				materialGrass = materialGrass,
				grassUpDotThreshold = grassUpDotThreshold,
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

				var sdfByte = clamp(sHemisphere, -127f, 127f);
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
			public float3 centerWorld;
			public float radius;
			public uint seed;
			public float noiseFrequency;
			public byte materialGround;
			public byte materialGold;
			public byte materialGrass;
			public float grassUpDotThreshold;

			public void Execute(int index)
			{
				// Reverse of linear index mapping: index = (x << X_SHIFT) + (y << Y_SHIFT) + z
				var x = (index >> X_SHIFT) & CHUNK_SIZE_MINUS_ONE;
				var y = (index >> Y_SHIFT) & CHUNK_SIZE_MINUS_ONE;
				var z = index & CHUNK_SIZE_MINUS_ONE;

				float3 localCoord = int3(x, y, z);
				var coord = bounds.Min + (localCoord * voxelSize);

				coord = transform(ltw, coord);

				var s = (float)volumeData.sdfVolume[index];

				// Surface normal approximation for hemisphere vs cap plane
				var toCenter = coord - centerWorld;
				var d = max(1e-6f, length(toCenter));
				var sSphere = radius - d;
				var sPlane = centerWorld.y - coord.y;
				var sphereSurface = sSphere <= sPlane;
				var normal = sphereSurface ? toCenter / d : new float3(0f, 1f, 0f);

				// Outside (air): pad one-voxel shell with material to support blending
				if (s < 0f)
				{
					if (abs(s) <= voxelSize)
					{
						var padMat = normal.y > grassUpDotThreshold ? materialGrass : materialGround;
						volumeData.materials[index] = padMat;
					}
					else
					{
						volumeData.materials[index] = MATERIAL_AIR;
					}

					return;
				}

				// Inside: choose gold vs ground by secondary 2D noise
				var seed2 = new float2(seed, seed);
				var n2 = cnoise((coord.xz * noiseFrequency * 2f) + (seed2 + 100f));
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
					// Up-facing surfaces become grass
					mat = normal.y > grassUpDotThreshold ? materialGrass : materialGround;

				volumeData.materials[index] = mat;
			}
		}
	}
}
