namespace Voxels.Core.Stamps
{
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using static Unity.Mathematics.Geometry.Math;
	using static Unity.Mathematics.math;
	using static VoxelConstants;

	//sphere only
	[BurstCompile]
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
		public float4x4 volumeWtl;

		public void Execute()
		{
			// Transform stamp bounds from world space to local volume space
			var localStampBounds = Transform(volumeWtl, stamp.bounds);

			// Convert to voxel coordinates relative to volume bounds
			var localMin = (localStampBounds.Min - localVolumeBounds.Min) * rcp(voxelSize);
			var localMax = (localStampBounds.Max - localVolumeBounds.Min) * rcp(voxelSize);
			var localBounds = new MinMaxAABB(localMin, localMax);

			var vMin = (int3)floor(localBounds.Min);
			var vMax = (int3)ceil(localBounds.Max);

			vMin = max(vMin, 0);
			vMax = min(vMax, CHUNK_SIZE - 1);

			// Transform sphere center from world space to local volume space
			var localSphereCenter = transform(volumeWtl, stamp.shape.sphere.center);

			// Convert sphere center to voxel coordinates
			var voxelSphereCenter = (localSphereCenter - localVolumeBounds.Min) * rcp(voxelSize);

			// Calculate transformed radius accounting for scale in the transformation
			// Extract uniform scale from the transformation matrix
			var scale = length(volumeWtl.c0.xyz); // Assuming uniform scale
			var radiusVoxel = stamp.shape.sphere.radius * scale * rcp(voxelSize);
			var r2 = radiusVoxel * radiusVoxel;
			var invRadius = 1f / radiusVoxel;

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

				var weight = saturate(1f - (sqrt(d2) * invRadius));

				volumeSdf[ptr] = (sbyte)clamp(
					//
					round(
						//
						lerp(
							volumeSdf[ptr],
							select(-127, 127, stamp.strength >= 0),
							weight * abs(stamp.strength)
						)
					),
					-127,
					127
				);
				volumeMaterials[ptr] = (byte)select((int)MATERIAL_AIR, stamp.material, volumeSdf[ptr] >= 0);
			}
		}
	}
}
