namespace Voxels.Core.Procedural.Generators.Materials
{
	using System;
	using Spatial;
	using Unity.Burst;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using UnityEngine;
	using static Unity.Mathematics.math;
	using static Unity.Mathematics.noise;
	using static VoxelConstants;

	[Serializable]
	public sealed class LayeredCoreMaterialGenerator : ProceduralMaterialGeneratorBehaviour
	{
		[Header("Geoid Base Shape")]
		[Tooltip("Base radius for the geoid in world units. Scales both sphere and star components.")]
		[SerializeField]
		[Range(2f, 1000f)]
		float radius = 20f;

		[Tooltip("World-space center of the geoid (origin for sphere and star prism).")]
		[SerializeField]
		Vector3 center = Vector3.zero;

		[Tooltip("Noise frequency scale (1/frequency). Reserved for noise-based features.")]
		[SerializeField]
		[Range(0.1f, 50)]
		float noiseFrequency = 16f;

		[Header("Materials")]
		[Tooltip("Material ID to use for generic ground/dirt areas and fallback cases.")]
		[SerializeField]
		byte materialGround = 1;

		[Tooltip("Material ID for optional ore/precious material (currently not used).")]
		[SerializeField]
		byte materialGold = 2;

		[Tooltip("Material ID used for grass on up-facing surface normals.")]
		[SerializeField]
		byte materialGrass = 3;

		[Header("Grass Painting")]
		[Tooltip(
			"Minimum up-facing normal dot (n.y) required to paint grass. 1 = only perfectly up-facing."
		)]
		[SerializeField]
		float grassUpDotThreshold = 0.7f;

		[Tooltip(
			"Range subtracted from threshold to widen grass coverage. Effective threshold = threshold - range."
		)]
		[SerializeField]
		[Range(0f, 1f)]
		float grassUpDotRange = .25f;

		[Header("Grass Noise")]
		[Tooltip("XZ-projected 2D noise frequency controlling variation of grass coverage.")]
		[SerializeField]
		[Range(0.01f, 64f)]
		float grassNoiseFrequency = 2f;

		[Tooltip("World-space XZ offset applied to the grass noise pattern.")]
		[SerializeField]
		Vector2 grassNoiseOffset = Vector2.zero;

		[Header("Quantization / SDF Packing")]
		[Tooltip("Linear samples per voxel used to quantize SDF into sbyte. Higher = finer precision.")]
		[SerializeField]
		[Range(1, 64)]
		int sdfSamplesPerVoxel = 16;

		[Header("Inside Gradient (Surface→Core)")]
		[Tooltip("Material at the interior surface boundary (start of gradient).")]
		[SerializeField]
		byte insideMaterialSurface = 1;

		[Tooltip("Material for the deep interior (end of gradient).")]
		[SerializeField]
		byte insideMaterialCore = 2;

		[Tooltip(
			"Core radius measured from the world-space center; gradient spans from surface radius down to this radius."
		)]
		[SerializeField]
		[Range(0f, 500f)]
		float coreRadius = 1f;

		[Tooltip("Min multiplier for noise modulation of core blend factor t.")]
		[SerializeField]
		[Range(0f, 2f)]
		float materialBlendNoiseMin = 0.75f;

		[Tooltip("Max multiplier for noise modulation of core blend factor t.")]
		[SerializeField]
		[Range(0f, 2f)]
		float materialBlendNoiseMax = 1.25f;

		public override JobHandle ScheduleMaterials(
			MinMaxAABB localBounds,
			float4x4 ltw,
			float voxelSize,
			VoxelVolumeData data,
			JobHandle inputDeps
		)
		{
			// Linear quantization scale to pack more resolution per voxel into sbyte
			var sdfScale = sdfSamplesPerVoxel / voxelSize;

			inputDeps = new MaterialPaintJob
			{
				ltw = ltw,
				bounds = localBounds,
				voxelSize = voxelSize,
				volumeData = data,
				centerWorld = center,
				radius = radius,
				noiseFrequency = rcp(noiseFrequency),
				materialGrass = materialGrass,
				grassUpDotThreshold = grassUpDotThreshold,
				grassUpDotRange = grassUpDotRange,
				sdfScale = sdfScale,
				insideMaterialSurface = insideMaterialSurface,
				insideMaterialCore = insideMaterialCore,
				coreRadius = coreRadius,
				materialBlendNoiseMin = materialBlendNoiseMin,
				materialBlendNoiseMax = materialBlendNoiseMax,
				grassNoiseFrequency = grassNoiseFrequency,
				grassNoiseOffset = new float2(grassNoiseOffset.x, grassNoiseOffset.y),
			}.Schedule(VOLUME_LENGTH, inputDeps);

			return inputDeps;
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
			public float noiseFrequency;
			public byte materialGrass;
			public float grassUpDotThreshold;
			public float sdfScale;
			public byte insideMaterialSurface;
			public byte insideMaterialCore;
			public float coreRadius;
			public float materialBlendNoiseMin;
			public float materialBlendNoiseMax;
			public float grassUpDotRange;
			public float grassNoiseFrequency;
			public float2 grassNoiseOffset;

			public void Execute(int index)
			{
				// Reverse of linear index mapping: index = (x << X_SHIFT) + (y << Y_SHIFT) + z
				var x = (index >> X_SHIFT) & CHUNK_SIZE_MINUS_ONE;
				var y = (index >> Y_SHIFT) & CHUNK_SIZE_MINUS_ONE;
				var z = index & CHUNK_SIZE_MINUS_ONE;

				float3 localCoord = int3(x, y, z);
				var coord = bounds.Min + (localCoord * voxelSize);

				coord = transform(ltw, coord);

				// Adopt convention: negative = inside, positive = outside (air)
				var sNegInside = -(volumeData.sdfVolume[index] / sdfScale);

				// Surface normal approximation for hemisphere vs cap plane
				var toCenter = coord - centerWorld;
				var d = max(1e-6f, length(toCenter));

				// Outside (air): pad one-voxel shell with material to support blending
				if (sNegInside > 0f)
				{
					if (abs(sNegInside) <= voxelSize)
					{
						var nPad = SdfGradients.EstimateNormalFromVolume(volumeData.sdfVolume, x, y, z);
						var noiseValPad = cnoise((coord.xz * grassNoiseFrequency) + grassNoiseOffset);
						var noiseNormPad = saturate(0.5f + (0.5f * noiseValPad));
						var grassThreshPad = clamp(
							grassUpDotThreshold - (grassUpDotRange * noiseNormPad),
							-1f,
							1f
						);
						var padMat = nPad.y >= grassThreshPad ? materialGrass : insideMaterialSurface;
						volumeData.materials[index] = padMat;
					}
					else
					{
						volumeData.materials[index] = MATERIAL_AIR;
					}

					return;
				}

				// Inside: blend across the full range [surfaceRadius .. coreRadius]
				var radial = d; // distance from centerWorld
				var surfaceRadius = radius;
				var denom = max(surfaceRadius - coreRadius, 1e-6f);
				var t = saturate((surfaceRadius - radial) / denom);
				// Add subtle 3D snoise modulation to the blend using noiseFrequency
				var n3 = cnoise(coord * noiseFrequency);
				var n01 = saturate(0.5f + (0.5f * n3));
				var tNoise = saturate(t * lerp(materialBlendNoiseMin, materialBlendNoiseMax, n01));
				var insideMat = (byte)floor(lerp(insideMaterialSurface, insideMaterialCore, tNoise));

				// Near-surface: check sign change in 6-neighborhood using SDF volume
				var nearAir = false;
				// X+1
				if (!nearAir && x < CHUNK_SIZE_MINUS_ONE)
				{
					var neighbor = -(float)volumeData.sdfVolume[index + (1 << X_SHIFT)];
					if (neighbor > 0f)
						nearAir = true;
				}

				// X-1
				if (!nearAir && x > 0)
				{
					var neighbor = -(float)volumeData.sdfVolume[index - (1 << X_SHIFT)];
					if (neighbor > 0f)
						nearAir = true;
				}

				// Y+1
				if (!nearAir && y < CHUNK_SIZE_MINUS_ONE)
				{
					var neighbor = -(float)volumeData.sdfVolume[index + (1 << Y_SHIFT)];
					if (neighbor > 0f)
						nearAir = true;
				}

				// Y-1
				if (!nearAir && y > 0)
				{
					var neighbor = -(float)volumeData.sdfVolume[index - (1 << Y_SHIFT)];
					if (neighbor > 0f)
						nearAir = true;
				}

				// Z+1
				if (!nearAir && z < CHUNK_SIZE_MINUS_ONE)
				{
					var neighbor = -(float)volumeData.sdfVolume[index + 1];
					if (neighbor > 0f)
						nearAir = true;
				}

				// Z-1
				if (!nearAir && z > 0)
				{
					var neighbor = -(float)volumeData.sdfVolume[index - 1];
					if (neighbor > 0f)
						nearAir = true;
				}

				var mat = insideMat;
				if (nearAir)
				{
					// Use SDF gradient normal to decide surface material: up-facing -> grass, otherwise gradient material
					var n = SdfGradients.EstimateNormalFromVolume(volumeData.sdfVolume, x, y, z);
					var noiseVal = cnoise((coord.xz * grassNoiseFrequency) + grassNoiseOffset);
					var noiseNorm = saturate(0.5f + (0.5f * noiseVal));
					var grassThresh = clamp(grassUpDotThreshold - (grassUpDotRange * noiseNorm), -1f, 1f);
					mat = n.y >= grassThresh ? materialGrass : insideMat;
				}

				volumeData.materials[index] = mat;
			}
		}
	}
}
