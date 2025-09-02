namespace Voxels.Editor
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using UnityEditor;
	using UnityEditor.Build;
#if UNITY_2021_2_OR_NEWER
#endif

	public static class ScriptingDefineUtility
	{
		public const string VMF_TAIL_PIPELINE = "VMF_TAIL_PIPELINE";

		public static bool SetTailPipelineDefineEnabled(bool enabled)
		{
			// Prefer updating only the active target to avoid heavy recompiles while editing settings
			return SetDefineEnabledForActiveTarget(VMF_TAIL_PIPELINE, enabled);
		}

		public static bool SetTailPipelineDefineEnabledAllTargets(bool enabled)
		{
			return SetDefineEnabledAllTargets(VMF_TAIL_PIPELINE, enabled);
		}

		public static bool SetDefineEnabledAllTargets(string define, bool enable)
		{
			var changed = false;
			foreach (var group in EnumerateBuildTargetGroups())
				changed |= SetDefineEnabled(group, define, enable);
			return changed;
		}

		public static bool SetDefineEnabledForActiveTarget(string define, bool enable)
		{
			var group = EditorUserBuildSettings.selectedBuildTargetGroup;
			return SetDefineEnabled(group, define, enable);
		}

		public static bool IsDefineEnabledForActiveTarget(string define)
		{
			var group = EditorUserBuildSettings.selectedBuildTargetGroup;
			var defines = new HashSet<string>(
				GetDefines(group).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
			);
			return defines.Contains(define);
		}

		static IEnumerable<BuildTargetGroup> EnumerateBuildTargetGroups()
		{
			foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)))
			{
				if (group == BuildTargetGroup.Unknown)
					continue;
				// Skip obsolete groups (string check to avoid API diffs across versions)
				var name = group.ToString();
				if (name.Equals("Deprecated", StringComparison.OrdinalIgnoreCase))
					continue;
				yield return group;
			}
		}

		static bool SetDefineEnabled(BuildTargetGroup group, string define, bool enable)
		{
			var defines = new HashSet<string>(
				GetDefines(group).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
			);
			var has = defines.Contains(define);
			if (enable && !has)
			{
				defines.Add(define);
				SetDefines(group, string.Join(";", defines.OrderBy(d => d)));
				return true;
			}

			if (!enable && has)
			{
				defines.Remove(define);
				SetDefines(group, string.Join(";", defines.OrderBy(d => d)));
				return true;
			}

			return false;
		}

		static string GetDefines(BuildTargetGroup group)
		{
#if UNITY_2021_2_OR_NEWER
			var named = NamedBuildTarget.FromBuildTargetGroup(group);
			return PlayerSettings.GetScriptingDefineSymbols(named);
#else
			return PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
#endif
		}

		static void SetDefines(BuildTargetGroup group, string defines)
		{
#if UNITY_2021_2_OR_NEWER
			var named = NamedBuildTarget.FromBuildTargetGroup(group);
			PlayerSettings.SetScriptingDefineSymbols(named, defines);
#else
			PlayerSettings.SetScriptingDefineSymbolsForGroup(group, defines);
#endif
		}
	}
}
