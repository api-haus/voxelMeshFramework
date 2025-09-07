namespace Voxels.Core.ThirdParty.SurfaceNets.Extensions
{
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using UnityEngine;

	public static class MinMaxAABBBoundsExtension
	{
		public static Bounds ToBounds(this MinMaxAABB b)
		{
			return new Bounds((b.Min + b.Max) * .5f, b.Max - b.Min);
		}

		public static MinMaxAABB Transform(this MinMaxAABB aabb, float4x4 trs)
		{
			return Math.Transform(trs, aabb);
		}

		public static MinMaxAABB Transform(this MinMaxAABB aabb, float3x3 trs)
		{
			return Math.Transform(trs, aabb);
		}
	}
}
