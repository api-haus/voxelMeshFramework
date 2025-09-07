namespace Voxels.Core.Atlasing.Components
{
	using System;
	using Unity.Entities;
	using Unity.Mathematics;

	public struct AtlasedChunk : IComponentData, IEquatable<AtlasedChunk>
	{
		public int3 coord;
		public int atlasId;

		public bool Equals(AtlasedChunk other)
		{
			return coord.Equals(other.coord) && atlasId == other.atlasId;
		}

		public override bool Equals(object obj)
		{
			return obj is AtlasedChunk other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(coord, atlasId);
		}
	}
}
