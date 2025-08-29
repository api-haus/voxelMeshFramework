namespace Voxels.ThirdParty.SurfaceNets.Intrinsics
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
				var ab_and = vandq_u8(a, b);
				var ab_andnot = vandq_u8(a, vmvnq_u8(b)); // ~b

				// Combine results into a single bitmask
				var or_result = vorrq_u8(ab_and, ab_andnot);

				// Use vmaxvq_u8 to check if any non-zero
				return vmaxvq_u8(or_result) != 0 ? 1 : 0;
			}

			return -1;
		}
	}
}
