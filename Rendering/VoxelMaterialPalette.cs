namespace Rendering
{
	using Adobe.Substance;
	using UnityEngine;

	[CreateAssetMenu(menuName = "🧊Voxel Meshing/Material Palette 🎨")]
	public class VoxelMaterialPalette : ScriptableObject
	{
#if ADOBE_SUBSTANCE3D
		[SerializeField]
		SubstanceGraphSO[] substanceGraphs;
#endif

		[SerializeField]
		int resolution = 1024;
	}
}
