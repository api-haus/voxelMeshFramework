namespace Voxels.Core.Authoring
{
	using Unity.Burst;
	using UnityEngine;

	public class VoxelDebugging : MonoBehaviour
	{
		public static readonly SharedStatic<bool> Enabled =
			SharedStatic<bool>.GetOrCreate<VoxelDebugging>();

		[Tooltip("Only works if ALINE is present.")]
		[SerializeField]
		bool visualGizmos = true;

		public static ref bool IsEnabled => ref Enabled.Data;

		void OnValidate()
		{
			Enabled.Data = visualGizmos;
		}
	}
}
