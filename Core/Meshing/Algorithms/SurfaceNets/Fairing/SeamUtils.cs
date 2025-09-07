namespace Voxels.Core.Meshing.Algorithms.SurfaceNets.Fairing
{
	using System.Runtime.CompilerServices;
	using Unity.Mathematics;
	using static VoxelConstants;

	/// <summary>
	///   Seam and overlap slab helpers for deterministic cross-chunk behavior.
	/// </summary>
	static class SeamUtils
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsInLowSlab(int coord)
		{
			return coord < CHUNK_OVERLAP;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsInHighSlab(int coord)
		{
			return coord >= CHUNK_SIZE - CHUNK_OVERLAP;
		}

		/// <summary>
		///   True if a cell lies in any overlap slab along any axis.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsInAnySeamSlab(in int3 c)
		{
			return IsInLowSlab(c.x)
				|| IsInHighSlab(c.x)
				|| IsInLowSlab(c.y)
				|| IsInHighSlab(c.y)
				|| IsInLowSlab(c.z)
				|| IsInHighSlab(c.z);
		}

		/// <summary>
		///   Returns true if neighbor cell remains within the same overlap slab(s)
		///   as the source for each axis where the source is in a slab.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool NeighborRespectsSeamSlab(in int3 src, in int3 n)
		{
			// X axis
			if (IsInLowSlab(src.x) && !IsInLowSlab(n.x))
				return false;
			if (IsInHighSlab(src.x) && !IsInHighSlab(n.x))
				return false;
			// Y axis
			if (IsInLowSlab(src.y) && !IsInLowSlab(n.y))
				return false;
			if (IsInHighSlab(src.y) && !IsInHighSlab(n.y))
				return false;
			// Z axis
			if (IsInLowSlab(src.z) && !IsInLowSlab(n.z))
				return false;
			if (IsInHighSlab(src.z) && !IsInHighSlab(n.z))
				return false;
			return true;
		}

		/// <summary>
		///   Distance in cells to the nearest seam plane (0 at seam face, 1 inside slab, ...).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int DistanceToNearestSeamCell(in int3 c)
		{
			var dx = math.min(c.x, CHUNK_SIZE_MINUS_ONE - c.x);
			var dy = math.min(c.y, CHUNK_SIZE_MINUS_ONE - c.y);
			var dz = math.min(c.z, CHUNK_SIZE_MINUS_ONE - c.z);
			return math.min(dx, math.min(dy, dz));
		}
	}
}
