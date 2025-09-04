namespace Voxels.Core.Authoring
{
	using System;
	using Unity.Burst;
	using UnityEngine;

	public class VoxelDebugging : MonoBehaviour
	{
		static readonly SharedStatic<VisualDebugFlags> s_flags =
			SharedStatic<VisualDebugFlags>.GetOrCreate<VisualDebugFlags>();

		[Tooltip("Only works if ALINE is present.")]
		[SerializeField]
		bool visualGizmos = true;

		[SerializeField]
		VisualDebugFlags visualDebugFlags;

		public static ref bool IsEnabled => ref s_flags.Data.drawVisualGizmos;
		public static ref VisualDebugFlags Flags => ref s_flags.Data;

		void OnEnable()
		{
			OnValidate();
		}

		void OnValidate()
		{
			visualDebugFlags.drawVisualGizmos = visualGizmos;
			s_flags.Data = visualDebugFlags;
		}

		[Serializable]
		public struct VisualDebugFlags
		{
			[HideInInspector]
			public bool drawVisualGizmos;

			public bool spatialSystemGizmos;
			public bool stampGizmos;
		}
	}
}
