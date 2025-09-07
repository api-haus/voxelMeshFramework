namespace Rendering
{
	using UnityEngine.Rendering;
#if URP_14_0_0
	using UnityEngine.Rendering.Universal;
#endif
#if HDRP_14_0_0
	using UnityEngine.Rendering.HighDefinition;
#endif

	public static class RPIntel
	{
		public static ActiveRP DetermineActiveRP()
		{
			return GraphicsSettings.currentRenderPipeline switch
			{
#if HDRP_14_0_0
				HDRenderPipelineAsset => ActiveRP.HIGH_DEFINITION,
#endif
#if URP_14_0_0
				UniversalRenderPipelineAsset => ActiveRP.UNIVERSAL,
#endif
				_ => ActiveRP.BUILT_IN,
			};
		}
	}
}
