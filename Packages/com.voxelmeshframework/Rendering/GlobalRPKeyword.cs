namespace Rendering
{
	using UnityEngine;
	using UnityEngine.Rendering;

	public static class GlobalRPKeyword
	{
		static GlobalKeyword GetRPKeyword(ActiveRP rp)
		{
			var rpHDRP = GlobalKeyword.Create("_RP_HDRP");
			var rpURP = GlobalKeyword.Create("_RP_URP");
			var rpBIRP = GlobalKeyword.Create("_RP_BIRP");

			switch (rp)
			{
				case ActiveRP.HIGH_DEFINITION:
					return rpHDRP;
				default:
				case ActiveRP.BUILT_IN:
					return rpBIRP;
				case ActiveRP.UNIVERSAL:
					return rpURP;
			}
		}

		[RuntimeInitializeOnLoadMethod]
		public static void SetGlobalRPKeywords()
		{
			var active = RPIntel.DetermineActiveRP();

			if (ActiveRP.HIGH_DEFINITION != active)
				Shader.DisableKeyword(GetRPKeyword(ActiveRP.HIGH_DEFINITION));
			if (ActiveRP.UNIVERSAL != active)
				Shader.DisableKeyword(GetRPKeyword(ActiveRP.UNIVERSAL));
			if (ActiveRP.BUILT_IN != active)
				Shader.DisableKeyword(GetRPKeyword(ActiveRP.BUILT_IN));

			Shader.EnableKeyword(GetRPKeyword(active));
		}
	}
}
