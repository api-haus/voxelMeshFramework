namespace Voxels.Tests.Editor
{
	using Core.Spatial;
	using Core.Stamps;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using static Core.VoxelConstants;

	[TestFixture]
	public class VoxelStampIntegrationTests
	{
		[Test]
		public void QueryThenApply_ModifiesIntersectingVolume()
		{
			// Build a spatial object with identity transform and allocated data
			var volume = new VoxelVolumeData(Allocator.TempJob) { voxelSize = 1f };
			try
			{
				var spatial = new SpatialVoxelObject
				{
					entity = default,
					localBounds = new MinMaxAABB(float3.zero, new float3(CHUNK_SIZE)),
					voxelSize = 1f,
					ltw = float4x4.identity,
					wtl = float4x4.identity,
				};

				var sh = new VoxelSpatialSystem.VoxelObjectHash
				{
					hash = new NativeParallelMultiHashMap<int3, SpatialVoxelObject>(64, Allocator.Temp),
				};

				try
				{
					sh.Add(spatial);

					// Issue a world-space stamp centered at local center (world==local here)
					var stamp = new NativeVoxelStampProcedural
					{
						shape = new ProceduralShape
						{
							shape = ProceduralShape.Shape.SPHERE,
							sphere = new ProceduralSphere { center = new float3(16, 16, 16), radius = 4f },
						},
						bounds = new MinMaxAABB(new float3(12, 12, 12), new float3(20, 20, 20)),
						strength = 1f,
						material = 5,
					};

					using var candidates = sh.Query(stamp.bounds, Allocator.Temp);
					Assert.AreEqual(1, candidates.Length);

					var cand = candidates[0];
					var sdf = volume.sdfVolume;
					var mat = volume.materials;

					var job = new ApplyVoxelStampJob
					{
						stamp = stamp,
						volumeSdf = sdf,
						volumeMaterials = mat,
						localVolumeBounds = cand.localBounds,
						voxelSize = cand.voxelSize,
						volumeLTW = cand.ltw,
						volumeWtl = cand.wtl,
					};

					job.Schedule().Complete();

					var idx = (16 << X_SHIFT) + (16 << Y_SHIFT) + 16;
					Assert.AreEqual(127, sdf[idx]);
					Assert.AreEqual(5, mat[idx]);
				}
				finally
				{
					sh.Dispose();
				}
			}
			finally
			{
				volume.Dispose();
			}
		}
	}
}
