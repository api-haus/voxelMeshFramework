namespace PostEffects
{
	using UnityEngine;

	static class Ext
	{
		public static RenderTextureFormat argbHalf = RenderTextureUtils.GetSupportedFormat(
			RenderTextureFormat.ARGBHalf
		);
	}
}
