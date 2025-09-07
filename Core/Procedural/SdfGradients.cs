namespace Voxels.Core.Procedural
{
	using System.Runtime.CompilerServices;
	using Unity.Collections;
	using Unity.Mathematics;
	using static Unity.Mathematics.math;
	using static VoxelConstants;

	/// <summary>
	/// 	SDF gradient/normal estimation helpers.
	/// 	Provides Burst-friendly, branch-light central-difference normal estimation from the SDF volume,
	/// 	and a utility matching the 8-corner sample gradient used by Surface Nets.
	/// </summary>
	public static class SdfGradients
	{
		/// <summary>
		/// 	Estimate outward normal from the SDF volume using finite differences.
		/// 	Works with the project's inside-positive SDF convention by negating the gradient.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float3 EstimateNormalFromVolume(NativeArray<sbyte> sdfVolume, int x, int y, int z)
		{
			// Central differences where possible, forward/backward on borders
			var x0 = max(0, x - 1);
			var x1 = min(CHUNK_SIZE_MINUS_ONE, x + 1);
			var y0 = max(0, y - 1);
			var y1 = min(CHUNK_SIZE_MINUS_ONE, y + 1);
			var z0 = max(0, z - 1);
			var z1 = min(CHUNK_SIZE_MINUS_ONE, z + 1);

			int idx(int xi, int yi, int zi)
			{
				return (xi << X_SHIFT) + (yi << Y_SHIFT) + zi;
			}

			var sx0 = (float)sdfVolume[idx(x0, y, z)];
			var sx1 = (float)sdfVolume[idx(x1, y, z)];
			var sy0 = (float)sdfVolume[idx(x, y0, z)];
			var sy1 = (float)sdfVolume[idx(x, y1, z)];
			var sz0 = (float)sdfVolume[idx(x, y, z0)];
			var sz1 = (float)sdfVolume[idx(x, y, z1)];

			var grad = new float3(sx1 - sx0, sy1 - sy0, sz1 - sz0);
			// Outward = -gradient for inside-positive SDF
			var n = -grad;
			var len2 = dot(n, n);
			if (len2 < 1e-12f)
				return new float3(0f, 1f, 0f);
			return n * rsqrt(len2);
		}

		/// <summary>
		/// 	Estimate outward normal from 8 cube-corner SDF samples (order matches Surface Nets in NaiveSurfaceNets).
		/// 	The returned vector is normalized.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe float3 EstimateNormalFromEightCorners(float* samples)
		{
			float3 grad;
			// Match NaiveSurfaceNets corner layout and form central differences per axis
			grad.z =
				samples[4]
				- samples[0]
				+ (samples[5] - samples[1])
				+ (samples[6] - samples[2])
				+ (samples[7] - samples[3]);

			grad.y =
				samples[2]
				- samples[0]
				+ (samples[3] - samples[1])
				+ (samples[6] - samples[4])
				+ (samples[7] - samples[5]);

			grad.x =
				samples[1]
				- samples[0]
				+ (samples[3] - samples[2])
				+ (samples[5] - samples[4])
				+ (samples[7] - samples[6]);

			// Outward = -gradient for inside-positive SDF
			var n = -grad;
			var len2 = dot(n, n);
			if (len2 < 1e-12f)
				return new float3(0f, 1f, 0f);
			return n * rsqrt(len2);
		}
	}
}
