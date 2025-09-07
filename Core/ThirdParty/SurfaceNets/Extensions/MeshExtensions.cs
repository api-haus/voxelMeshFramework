namespace Voxels.Core.ThirdParty.SurfaceNets.Extensions
{
	using UnityEngine.Rendering;

	public static class MeshExtensions
	{
		public const MeshUpdateFlags NO_UPDATE_FLAGS =
			MeshUpdateFlags.DontNotifyMeshUsers
			| // no need probably ?
			MeshUpdateFlags.DontRecalculateBounds
			| // bounds are calculated in job
			MeshUpdateFlags.DontResetBoneBounds
			| //
			MeshUpdateFlags.DontValidateIndices; // they are probably fine ;)
	}
}
