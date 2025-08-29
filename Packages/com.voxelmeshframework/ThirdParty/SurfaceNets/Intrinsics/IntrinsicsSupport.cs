namespace Voxels.ThirdParty.SurfaceNets.Intrinsics
{
	using Unity.Burst;
	using UnityEngine;
	using static Unity.Burst.Intrinsics.Arm.Neon;
	using static Unity.Burst.Intrinsics.X86.Avx;
	using static Unity.Burst.Intrinsics.X86.Avx2;

	[BurstCompile]
	public static class IntrinsicsSupport
	{
		[BurstCompile]
		public static void DebugSupport()
		{
			if (IsNeonSupported)
				Debug.Log("IsNeonSupported = true");
			if (IsNeonArmv82FeaturesSupported)
				Debug.Log("IsNeonArmv82FeaturesSupported = true");
			if (IsNeonCryptoSupported)
				Debug.Log("IsNeonCryptoSupported = true");
			if (IsNeonDotProdSupported)
				Debug.Log("IsNeonDotProdSupported = true");
			if (IsNeonRDMASupported)
				Debug.Log("IsNeonRDMASupported = true");
			if (IsAvx2Supported)
				Debug.Log("IsAvx2Supported = true");
			if (IsAvxSupported)
				Debug.Log("IsAvxSupported = true");
		}
	}
}
