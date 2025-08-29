namespace Voxels.Core.Stamps
{
	using Debugging;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
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

		public MinMaxAABB volumeBounds;

		public float voxelSize;

		[ReadOnly]
		public NativeVoxelStampProcedural stamp;

		public void Execute()
		{
			var localMin = stamp.bounds.Min - volumeBounds.Min;
			var localMax = localMin + stamp.bounds.Extents;
			var localBounds = new MinMaxAABB(localMin, localMax);

			var vMin = (int3)floor(localBounds.Min);
			var vMax = (int3)ceil(localBounds.Max);

			vMin = max(vMin, 0);
			vMax = min(vMax, CHUNK_SIZE - 1);

			var vCenter = (float3)(vMax + vMin) / 2f;

			var radiusVoxel = stamp.shape.sphere.radius;
			var r2 = radiusVoxel * radiusVoxel;
			var invRadius = 1f / radiusVoxel;

			for (var x = vMin.x; x <= vMax.x; x++)
			for (var y = vMin.y; y <= vMax.y; y++)
			for (var z = vMin.z; z <= vMax.z; z++)
			{
				var coord = new float3(x, y, z);
				var diff = coord - vCenter;
				var d2 = dot(diff, diff);
				if (d2 > r2)
					continue;

				// ===== MEMORY ADDRESS CALCULATION =====
				// Calculate the base pointer for the current voxel row.
				// X_SHIFT and Y_SHIFT are bit shifts corresponding to chunk dimensions.
				var ptr = (x << X_SHIFT) + (y << Y_SHIFT) + z;

				var weight = saturate(1f - (sqrt(d2) * invRadius));

#if ALINE && DEBUG
				Visual.Draw.PushDuration(1f);
				Visual.Draw.WireBox(volumeBounds.Min + (coord * voxelSize), voxelSize);
				Visual.Draw.PopDuration();
#endif

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
			}
		}
	}
}
