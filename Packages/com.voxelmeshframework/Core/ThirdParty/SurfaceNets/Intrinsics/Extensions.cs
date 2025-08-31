namespace Voxels.Core.ThirdParty.SurfaceNets.Intrinsics
{
	using Unity.Burst.Intrinsics;
	using static Unity.Burst.Intrinsics.Arm.Neon;

	public static class NeonExt
	{
		public static int test_mix_ones_zeroesNEON(v128 a, v128 mask)
		{
			return testnzc_si128NEON(a, mask);
		}

		public static int testnzc_si128NEON(v128 a, v128 b)
		{
			if (IsNeonSupported)
			{
				var abAnd = vandq_u8(a, b);
				var abAndnot = vandq_u8(a, vmvnq_u8(b)); // ~b

				// Combine results into a single bitmask
				var orResult = vorrq_u8(abAnd, abAndnot);

				// Use vmaxvq_u8 to check if any non-zero
				return vmaxvq_u8(orResult) != 0 ? 1 : 0;
			}

			return -1;
		}
	}
}
