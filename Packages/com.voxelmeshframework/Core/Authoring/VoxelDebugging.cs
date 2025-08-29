namespace Voxels.Core.Authoring
{
	using Unity.Burst;
	using UnityEngine;

	public class VoxelDebugging : MonoBehaviour
	{
		public static SharedStatic<bool> Enabled = SharedStatic<bool>.GetOrCreate<VoxelDebugging>();

		[Tooltip("Only works if ALINE is present.")]
		[SerializeField]
		bool visualGizmos = true;

		void OnValidate()
		{
			Enabled.Data = visualGizmos;
		}
	}
}
