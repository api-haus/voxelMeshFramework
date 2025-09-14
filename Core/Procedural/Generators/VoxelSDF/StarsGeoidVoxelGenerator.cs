namespace Voxels.Core.Procedural.Generators.VoxelSDF
{
	using System;
	using Spatial;
	using Unity.Burst;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using UnityEngine;
	using static sdf;
	using static Unity.Mathematics.float4x4;
	using static Unity.Mathematics.math;
	using static VoxelConstants;
	using quaternion = Unity.Mathematics.quaternion;

	[Serializable]
	public sealed class StarsGeoidVoxelGenerator : ProceduralVoxelGeneratorBehaviour
	{
		[Header("Geoid Base Shape")]
		[Tooltip("Base radius for the geoid in world units. Scales both sphere and star components.")]
		[SerializeField]
		[Range(2f, 1000f)]
		float radius = 20f;

		[Tooltip("World-space center of the geoid (origin for sphere and star prism).")]
		[SerializeField]
		Vector3 center = Vector3.zero;

		[Header("Quantization / SDF Packing")]
		[Tooltip("Linear samples per voxel used to quantize SDF into sbyte. Higher = finer precision.")]
		[SerializeField]
		[Range(1, 64)]
		int sdfSamplesPerVoxel = 16;

		[Header("Star Prism Shape")]
		[Tooltip(
			"Half-height of the 5-pointed star prism in world units (radius * value). Controls thickness."
		)]
		[SerializeField]
		[Range(0.01f, 8f)]
		float starRadiusHalfHeight = 1.1f;

		[Tooltip("Inner radius factor of the star (0..1). Lower values produce sharper points.")]
		[SerializeField]
		[Range(0.01f, 1f)]
		float starRadiusFactor = 1f;

		[Tooltip("Multiplier for the star radius. 1 = original radius.")]
		[SerializeField]
		[Range(0.01f, 10f)]
		float starRadiusMultiplier = 1f;

		[Header("Sphere Shape")]
		[Tooltip("Sphere radius as a factor of base radius (0..1). Blended (union) with star prism.")]
		[SerializeField]
		[Range(0.01f, 1f)]
		float sphereRadiusFactor = .7f;

		[Tooltip("Amount of stars to draw.")]
		[SerializeField]
		[Range(0, 12)]
		int numStars = 6;

		[Tooltip("Amount of stars to draw.")]
		[SerializeField]
		[Range(0, 90)]
		float starRotationDegrees = 16;

		[Tooltip("Star rotation axis.")]
		[SerializeField]
		float3 starAxis = up();

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
				starRadiusHalfHeight = starRadiusHalfHeight,
				starRadiusFactor = starRadiusFactor,
				sphereRadiusFactor = sphereRadiusFactor,
				starRadiusMultiplier = starRadiusMultiplier,
				numStars = numStars,
				starRotationDegrees = starRotationDegrees,
				starAxis = normalize(starAxis),
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
			public float starRadiusHalfHeight;
			public float starRadiusFactor;
			public float sphereRadiusFactor;
			public float starRadiusMultiplier;
			public int numStars;
			public float starRotationDegrees;
			public float3 starAxis;

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
				var sSphere = sdSphere(toCenter, radius * sphereRadiusFactor);

				var sStarSphereUnion = sSphere;

				for (var i = 0; i < numStars; i++)
				{
					var sStar = sdStar5Prism(
						// toCenter,
						opTx(
							TRS(0, quaternion.AxisAngle(starAxis, radians(i * starRotationDegrees)), 1),
							toCenter
						),
						radius * starRadiusMultiplier,
						starRadiusFactor,
						radius * starRadiusHalfHeight
					);
					sStarSphereUnion = opUnion(sStar, sStarSphereUnion);
				}

				var sdfByte = clamp(sStarSphereUnion * sdfScale, -127f, 127f);
				volumeData.sdfVolume[index] = (sbyte)sdfByte;
			}
		}
	}
}
