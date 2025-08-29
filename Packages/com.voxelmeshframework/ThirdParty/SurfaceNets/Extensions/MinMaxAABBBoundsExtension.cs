namespace Voxels.ThirdParty.SurfaceNets.Extensions
{
	using Unity.Mathematics.Geometry;
	using UnityEngine;

	public static class MinMaxAABBBoundsExtension
	{
		public static Bounds ToBounds(this MinMaxAABB b)
		{
			return new Bounds((b.Min + b.Max) * .5f, b.Max - b.Min);
		}
	}
}
