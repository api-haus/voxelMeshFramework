namespace Voxels.Core.Grids
{
	using System;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;

	public struct NativeVoxelChunk : IComponentData, IEquatable<NativeVoxelChunk>
	{
		public int3 coord;
		public int gridID;
		public float voxelSize;
		public MinMaxAABB bounds;

		public bool Equals(NativeVoxelChunk other)
		{
			return coord.Equals(other.coord) && gridID == other.gridID;
		}

		public override bool Equals(object obj)
		{
			return obj is NativeVoxelChunk other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(coord, gridID);
		}
	}
}
