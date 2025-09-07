namespace Voxels.Core.Stamps
{
	using System;
	using Atlasing.ChunkOverlaps;
	using Meshing.Components;
	using Spatial;
	using Unity.Jobs;
	using Unity.Mathematics;

	public static class StampScheduling
	{
		public static JobHandle ScheduleApplyStamp(
			in NativeVoxelStampProcedural stamp,
			in SpatialVoxelObject svo,
			in NativeVoxelMesh nvm,
			in StampApplyParams apply,
			JobHandle inputDeps = default
		)
		{
			var job = new ApplyVoxelStampJob
			{
				stamp = stamp,
				volumeSdf = nvm.volume.sdfVolume,
				volumeMaterials = nvm.volume.materials,
				localVolumeBounds = svo.localBounds,
				volumeLTW = svo.ltw,
				volumeWTL = svo.wtl,
				voxelSize = svo.voxelSize,
				sdfScale = apply.sdfScale,
				deltaTime = apply.deltaTime,
				alphaPerSecond = apply.alphaPerSecond,
			};

			return job.Schedule(inputDeps);
		}

		public static JobHandle ScheduleCopySharedOverlap(
			in NativeVoxelMesh src,
			in NativeVoxelMesh dst,
			byte axis,
			JobHandle inputDeps = default
		)
		{
			var job = new CopySharedOverlapJob
			{
				sourceSdf = src.volume.sdfVolume,
				sourceMaterials = src.volume.materials,
				destSdf = dst.volume.sdfVolume,
				destMaterials = dst.volume.materials,
				axis = axis,
			};

			return job.Schedule(inputDeps);
		}

		/// <summary>
		///   Resolve axis-aligned adjacency between two chunk coords and directionality.
		///   Returns true when adjacent along exactly one axis and equal on the others.
		/// </summary>
		public static bool TryResolveAdjacency(int3 a, int3 b, out byte axis, out bool aIsSource)
		{
			// +X / -X neighbors
			if (b.x == a.x + 1 && b.y == a.y && b.z == a.z)
			{
				axis = 0;
				aIsSource = true;
				return true;
			}

			if (a.x == b.x + 1 && a.y == b.y && a.z == b.z)
			{
				axis = 0;
				aIsSource = false;
				return true;
			}

			// +Y / -Y neighbors
			if (b.y == a.y + 1 && b.x == a.x && b.z == a.z)
			{
				axis = 1;
				aIsSource = true;
				return true;
			}

			if (a.y == b.y + 1 && a.x == b.x && a.z == b.z)
			{
				axis = 1;
				aIsSource = false;
				return true;
			}

			// +Z / -Z neighbors
			if (b.z == a.z + 1 && b.x == a.x && b.y == a.y)
			{
				axis = 2;
				aIsSource = true;
				return true;
			}

			if (a.z == b.z + 1 && a.x == b.x && a.y == b.y)
			{
				axis = 2;
				aIsSource = false;
				return true;
			}

			axis = 0;
			aIsSource = true;
			return false;
		}

		[Serializable]
		public struct StampApplyParams
		{
			public float sdfScale;
			public float deltaTime;
			public float alphaPerSecond;
		}
	}
}
