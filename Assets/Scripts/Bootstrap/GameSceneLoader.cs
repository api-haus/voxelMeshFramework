namespace Bootstrap
{
	using UnityEngine;
	using UnityEngine.SceneManagement;

	// Attach to an object in the root scene (build index 0). Loads the game scene additively by index.
	[DisallowMultipleComponent]
	public sealed class GameSceneLoader : MonoBehaviour
	{
		[SerializeField]
		int gameSceneBuildIndex = 2;

		[SerializeField]
		bool autoLoadOnStart = true;

		void Start()
		{
			if (autoLoadOnStart)
				LoadGameScene();
		}

		public void LoadGameScene()
		{
			if (gameSceneBuildIndex < 0)
				return;
			var path = SceneUtility.GetScenePathByBuildIndex(gameSceneBuildIndex);
			if (string.IsNullOrEmpty(path))
				return;
			var scene = SceneManager.GetSceneByPath(path);
			if (scene.IsValid() && scene.isLoaded)
				return;
			SceneManager.LoadSceneAsync(gameSceneBuildIndex, LoadSceneMode.Additive);
		}
	}
}
