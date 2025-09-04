namespace Voxels.Core.Stamps
{
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Mathematics.Geometry.Math;
	using static Unity.Mathematics.math;
	using static VoxelConstants;

	//sphere only
	[BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
	struct ApplyVoxelStampJob : IJob
	{
		[NativeDisableContainerSafetyRestriction]
		public NativeArray<sbyte> volumeSdf;

		[NativeDisableContainerSafetyRestriction]
		public NativeArray<byte> volumeMaterials;

		public MinMaxAABB localVolumeBounds;

		public float voxelSize;

		[ReadOnly]
		public NativeVoxelStampProcedural stamp;

		public float4x4 volumeLTW;
		public float4x4 volumeWTL;

		// Quantization: number of stored SDF steps per voxel
		public float sdfScale;

		// Time-based control
		public float deltaTime; // scheduler-provided
		public float alphaPerSecond; // how quickly to reach target at s=1

		public void Execute()
		{
			using var __ = VoxelStampSystem_ApplyStampJob.Auto();
			// Transform stamp bounds from world space to local volume space
			var localStampBounds = Transform(volumeWTL, stamp.bounds);

			// Convert to voxel coordinates relative to volume bounds
			var localMin = (localStampBounds.Min - localVolumeBounds.Min) * rcp(voxelSize);
			var localMax = (localStampBounds.Max - localVolumeBounds.Min) * rcp(voxelSize);
			var localBounds = new MinMaxAABB(localMin, localMax);

			var vMin = (int3)floor(localBounds.Min);
			var vMax = (int3)ceil(localBounds.Max);

			vMin = max(vMin, 0);
			vMax = min(vMax, CHUNK_SIZE - 1);

			// Transform sphere center from world space to local volume space
			var localSphereCenter = transform(volumeWTL, stamp.shape.sphere.center);

			// Convert sphere center to voxel coordinates
			var voxelSphereCenter = (localSphereCenter - localVolumeBounds.Min) * rcp(voxelSize);

			// Calculate transformed radius accounting for scale in the transformation
			// Extract uniform scale from the transformation matrix
			var scale = length(volumeWTL.c0.xyz); // Assuming uniform scale
			var radiusVoxel = stamp.shape.sphere.radius * scale * rcp(voxelSize);
			var r2 = radiusVoxel * radiusVoxel;
			var rcpRadius = rcp(radiusVoxel);

			for (var x = vMin.x; x <= vMax.x; x++)
			for (var y = vMin.y; y <= vMax.y; y++)
			for (var z = vMin.z; z <= vMax.z; z++)
			{
				var coord = new float3(x, y, z);
				var diff = coord - voxelSphereCenter;
				var d2 = dot(diff, diff);
				if (d2 > r2)
					continue;

				// ===== MEMORY ADDRESS CALCULATION =====
				// Calculate the base pointer for the current voxel row.
				// X_SHIFT and Y_SHIFT are bit shifts corresponding to chunk dimensions.
				var ptr = (x << X_SHIFT) + (y << Y_SHIFT) + z;

				var d = sqrt(d2);
				var weight = saturate(1f - (d * rcpRadius));

				// Existing SDF in world units
				var stored = (float)volumeSdf[ptr];
				var world = stored / sdfScale;

				// Sphere SDF in world units (inside-positive)
				var sSphereWorld = (radiusVoxel - d) * voxelSize;

				// Target by boolean SDF op in inside-positive convention
				var isPlace = stamp.strength >= 0f;
				var targetWorld = select(min(world, -sSphereWorld), max(world, sSphereWorld), isPlace);

				// Strength mapping: logarithmic response for fine control at low strengths + time scaling
				var strengthAbs = abs(stamp.strength);
				var s = clamp(strengthAbs, 0f, 1f);
				// alpha_base in [0,1]: 0->0, 1->1; k controls curve steepness (higher k => more sensitivity near 0)
				const float k = 5f;
				var alphaBase = select(0f, log((k * s) + 1f) / log(k + 1f), s > 0f);
				// convert per-second rate to per-step factor: alphaTime = 1 - exp(-rate * dt)
				var alphaTime = 1f - exp(-max(0f, alphaPerSecond) * max(0f, deltaTime));
				var alpha = s >= 1f ? 1f : alphaBase * weight * alphaTime;

				var worldNew = lerp(world, targetWorld, alpha);
				var storedNew = clamp(round(worldNew * sdfScale), -127f, 127f);

				// Ensure minimal effect: if quantization swallows the change, nudge by one LSB toward target
				if (alpha > 0f && storedNew == stored && targetWorld != world)
				{
					var dir = sign(targetWorld - world);
					storedNew = clamp(stored + select(-1f, 1f, dir > 0f), -127f, 127f);
				}

				volumeSdf[ptr] = (sbyte)storedNew;
				volumeMaterials[ptr] = (byte)select(
					MATERIAL_AIR,
					select(volumeMaterials[ptr], (int)stamp.material, stamp.strength >= 0),
					volumeSdf[ptr] >= 0
				);
			}
		}
	}
}
