using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bootstrap
{
	// Small payload in the root scene to load the UI scene additively (build index 1)
	[DisallowMultipleComponent]
	public sealed class RootSceneBootstrap : MonoBehaviour
	{
		[SerializeField]
		int uiSceneBuildIndex = 1;

		void Start()
		{
			TryLoadUiScene();
		}

		void TryLoadUiScene()
		{
			if (uiSceneBuildIndex < 0)
				return;
			var path = SceneUtility.GetScenePathByBuildIndex(uiSceneBuildIndex);
			if (string.IsNullOrEmpty(path))
				return;
			var scene = SceneManager.GetSceneByPath(path);
			if (scene.IsValid() && scene.isLoaded)
				return;
			SceneManager.LoadSceneAsync(uiSceneBuildIndex, LoadSceneMode.Additive);
		}
	}
}
