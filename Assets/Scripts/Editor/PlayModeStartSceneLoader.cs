// Ensures the scene at Build Index 0 is the Play Mode start scene in the Editor

namespace Editor
{
	using UnityEditor;

	[InitializeOnLoad]
	public static class PlayModeStartSceneLoader
	{
		static PlayModeStartSceneLoader()
		{
			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
		}

		static void OnPlayModeStateChanged(PlayModeStateChange change)
		{
			// if (Application.isBatchMode)
			// 	return;
			// if (change != PlayModeStateChange.ExitingEditMode)
			// 	return;
			//
			// var scene = SceneManager.GetActiveScene();
			// if (scene.name.Contains("InitTestScene", StringComparison.InvariantCultureIgnoreCase)) // a scene loaded from testing
			// 	return;
			//
			// var scenes = EditorBuildSettings.scenes;
			// if (scenes == null || scenes.Length == 0)
			// 	return;
			//
			// var firstScenePath = scenes[0].path;
			// if (string.IsNullOrEmpty(firstScenePath))
			// 	return;
			//
			// // Prefer PlayModeStartScene to avoid modifying the currently open scenes
			// var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(firstScenePath);
			// if (sceneAsset != null)
			// 	EditorSceneManager.playModeStartScene = sceneAsset;
		}
	}
}
