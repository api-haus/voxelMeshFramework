namespace Voxels.Editor
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using Core.Config;
	using UnityEditor;
	using UnityEngine;
	using UnityEngine.UIElements;

	public sealed class VoxelProjectSettingsProvider : SettingsProvider
	{
		SerializedObject m_SerializedSettings;
		VoxelProjectSettings m_SettingsAsset;

		public VoxelProjectSettingsProvider(string path, SettingsScope scope)
			: base(path, scope) { }

		public static bool IsSettingsAvailable()
		{
			return FindOrCreateSettingsAsset(false) != null;
		}

		[SettingsProvider]
		public static SettingsProvider CreateProvider()
		{
			var provider = new VoxelProjectSettingsProvider(
				"Project/Voxel Mesh Framework",
				SettingsScope.Project
			)
			{
				keywords = new HashSet<string>(new[] { "voxel", "mesh", "fence", "scheduling" }),
			};
			return provider;
		}

		public override void OnActivate(string searchContext, VisualElement rootElement)
		{
			m_SettingsAsset = FindOrCreateSettingsAsset(true);
			if (m_SettingsAsset != null)
				m_SerializedSettings = new SerializedObject(m_SettingsAsset);

			// Ensure scripting define reflects current settings on activation (deferred)
			// EditorApplication.delayCall += () =>
			// {
			// 	ScriptingDefineUtility.SetTailPipelineDefineEnabled(desiredTail);
			// };
		}

		public override void OnGUI(string searchContext)
		{
			if (m_SettingsAsset == null)
			{
				EditorGUILayout.HelpBox(
					"Create a VoxelProjectSettings asset to configure global settings.",
					MessageType.Info
				);
				if (GUILayout.Button("Create Settings Asset"))
				{
					m_SettingsAsset = FindOrCreateSettingsAsset(true, true);
					m_SerializedSettings = new SerializedObject(m_SettingsAsset);
				}

				return;
			}

			m_SerializedSettings.Update();
			// var policyProp = m_SerializedSettings.FindProperty("meshSchedulingPolicy");
			// EditorGUILayout.PropertyField(policyProp, new GUIContent("Mesh Scheduling Policy"));
			// EditorGUILayout.HelpBox(
			// 	"Please note that changing this setting will trigger a recompilation of the project.",
			// 	MessageType.None
			// );

			EditorGUILayout.PropertyField(
				m_SerializedSettings.FindProperty(nameof(VoxelProjectSettings.logLevelInGame)),
				new GUIContent("Log Level in Build")
			);
			EditorGUILayout.PropertyField(
				m_SerializedSettings.FindProperty(nameof(VoxelProjectSettings.logLevelInEditor)),
				new GUIContent("Log Level in Editor")
			);
			EditorGUILayout.Space();
			EditorGUILayout.PropertyField(
				m_SerializedSettings.FindProperty(nameof(VoxelProjectSettings.fenceRegistryCapacity)),
				new GUIContent("Fence Registry Capacity")
			);

			m_SerializedSettings.ApplyModifiedProperties();
			// if (m_SerializedSettings.ApplyModifiedProperties())
			// {
			// 	// Apply scripting define based on selected policy (deferred)
			// 	var enumVal = (MeshSchedulingPolicy)policyProp.enumValueIndex;
			// 	var desiredTail = enumVal == MeshSchedulingPolicy.TAIL_AND_PIPELINE;
			// 	EditorApplication.delayCall += () =>
			// 	{
			// 		ScriptingDefineUtility.SetTailPipelineDefineEnabled(desiredTail);
			// 	};
			// }
		}

		static VoxelProjectSettings FindOrCreateSettingsAsset(bool createIfMissing, bool ping = false)
		{
			const string path = "Assets/Settings/Resources/VoxelProjectSettings.asset";
			if (!Directory.Exists(Path.GetDirectoryName(path)))
				Directory.CreateDirectory(
					Path.GetDirectoryName(path) ?? throw new InvalidOperationException()
				);

			var settings = AssetDatabase.LoadAssetAtPath<VoxelProjectSettings>(path);
			if (settings == null && createIfMissing)
			{
				settings = ScriptableObject.CreateInstance<VoxelProjectSettings>();
				AssetDatabase.CreateAsset(settings, path);
				AssetDatabase.SaveAssets();
			}

			if (settings != null && ping)
				EditorGUIUtility.PingObject(settings);
			return settings;
		}
	}
}
