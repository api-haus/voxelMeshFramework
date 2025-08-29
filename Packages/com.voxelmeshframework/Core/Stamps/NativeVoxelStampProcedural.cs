namespace Voxels.Core.Stamps
{
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using UnityEngine;

	public struct ProceduralSphere
	{
		public float3 center;
		public float radius;
	}

	public struct ProceduralShape
	{
		public enum Shape
		{
			SPHERE,
		}

		public Shape shape;
		public ProceduralSphere sphere;
	}

	/// <summary>
	///   modifies sdf and material.
	///   intended for one-time use - deleted after one frame.
	/// </summary>
	public struct NativeVoxelStampProcedural : IComponentData
	{
		public ProceduralShape shape;
		public MinMaxAABB bounds;

		[Range(-1, 1)]
		public float strength;

		public byte material;
	}
}
