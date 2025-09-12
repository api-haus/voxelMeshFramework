namespace Voxels.Core.Meshing.Algorithms
{
	using UnityEngine;

	/// <summary>
	///   Controls how voxel materials are encoded per vertex.
	///   Single supported mode: corner-sum blended RGBA weights.
	/// </summary>
	public enum MaterialEncoding : byte
	{
		[InspectorName("No material encoding")]
		NONE = 0,

		/// <summary>
		///   Encode up to 4 material weights using corner-sum of the 8 cube corners (counts per material), normalized.
		/// </summary>
		[InspectorName("Encode material value in R channel R=0..255")]
		COLOR_DIRECT = 1,

		/// <summary>
		///   Palette color encoding from up to 256 material colors.
		/// </summary>
		[InspectorName("Palette color encoding from up to 256 different material colors")]
		COLOR_PALETTE = 2,

		/// <summary>
		///   Encode up to 4 material weights using corner-sum of the 8 cube corners (counts per material), normalized.
		/// </summary>
		[InspectorName("Splat-like color encoding for up to 4 materials")]
		COLOR_SPLAT_4 = 3,
	}
}
