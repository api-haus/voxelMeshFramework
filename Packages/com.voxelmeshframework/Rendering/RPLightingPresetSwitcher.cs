namespace Rendering
{
	using UnityEngine;

	[ExecuteAlways]
	public class RPLightingPresetSwitcher : MonoBehaviour
	{
		public GameObject highDefinitionPreset;
		public GameObject urpOrBuiltInPreset;

#if UNITY_EDITOR
		void Update()
		{
			UpdateRP();
		}
#endif

		void OnEnable()
		{
			UpdateRP();
		}

		GameObject GetActiveRoot(ActiveRP rp)
		{
			if (rp == ActiveRP.HIGH_DEFINITION)
				return highDefinitionPreset;
			return urpOrBuiltInPreset;
		}

		void UpdateRP()
		{
			var activeRP = RPIntel.DetermineActiveRP();
			var activeRoot = GetActiveRoot(activeRP);

			highDefinitionPreset.SetActive(false);
			urpOrBuiltInPreset.SetActive(false);
			activeRoot.SetActive(true);

			var skyLight = activeRoot.GetComponentInChildren<Light>();
			RenderSettings.sun = skyLight;

			var sb = activeRoot.GetComponentInChildren<Skybox>();
			RenderSettings.skybox = sb ? sb.material : null;

			if (activeRP != ActiveRP.HIGH_DEFINITION)
				skyLight.intensity = 1f;
		}
	}
}
