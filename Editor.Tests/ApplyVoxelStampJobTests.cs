namespace Voxels.Editor.Tests
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
	public class ApplyVoxelStampJobTests
	{
		static (VoxelVolumeData volume, NativeArray<sbyte> sdf, NativeArray<byte> mat) AllocateVolume()
		{
			var volume = new VoxelVolumeData(Allocator.TempJob);
			volume.voxelSize = 1f;
			return (volume, volume.sdfVolume, volume.materials);
		}

		[Test]
		public void SphereStamp_ModifiesCenterVoxel_AndSetsMaterial()
		{
			var (volume, sdf, mat) = AllocateVolume();
			try
			{
				var bounds = new MinMaxAABB(float3.zero, new float3(CHUNK_SIZE));
				var stamp = new NativeVoxelStampProcedural
				{
					shape = new ProceduralShape
					{
						shape = ProceduralShape.Shape.SPHERE,
						sphere = new ProceduralSphere { center = new float3(16, 16, 16), radius = 4f },
					},
					bounds = new MinMaxAABB(new float3(12, 12, 12), new float3(20, 20, 20)),
					strength = 1f,
					material = 7,
				};

				var job = new ApplyVoxelStampJob
				{
					stamp = stamp,
					volumeSdf = sdf,
					volumeMaterials = mat,
					localVolumeBounds = bounds,
					voxelSize = 1f,
					volumeLTW = float4x4.identity,
					volumeWTL = float4x4.identity,
					sdfScale = 32f,
				};

				job.Schedule().Complete();

				var idx = (16 << X_SHIFT) + (16 << Y_SHIFT) + 16;
				Assert.AreEqual(127, sdf[idx]);
				Assert.AreEqual(7, mat[idx]);
			}
			finally
			{
				volume.Dispose();
			}
		}

		[Test]
		public void SphereStamp_OutsideVoxelsUnaffected()
		{
			var (volume, sdf, mat) = AllocateVolume();
			try
			{
				var bounds = new MinMaxAABB(float3.zero, new float3(CHUNK_SIZE));
				var stamp = new NativeVoxelStampProcedural
				{
					shape = new ProceduralShape
					{
						shape = ProceduralShape.Shape.SPHERE,
						sphere = new ProceduralSphere { center = new float3(16, 16, 16), radius = 2f },
					},
					bounds = new MinMaxAABB(new float3(14, 14, 14), new float3(18, 18, 18)),
					strength = 1f,
					material = 3,
				};

				var job = new ApplyVoxelStampJob
				{
					stamp = stamp,
					volumeSdf = sdf,
					volumeMaterials = mat,
					localVolumeBounds = bounds,
					voxelSize = 1f,
					volumeLTW = float4x4.identity,
					volumeWTL = float4x4.identity,
					sdfScale = 32f,
				};

				job.Schedule().Complete();

				var outsideIdx = (0 << X_SHIFT) + (0 << Y_SHIFT) + 0;
				Assert.AreEqual(unchecked((sbyte)-128), sdf[outsideIdx]);
				Assert.AreEqual(0, mat[outsideIdx]);
			}
			finally
			{
				volume.Dispose();
			}
		}

		[Test]
		public void SphereStamp_UsesWTL_ToLocalizeWorldCenter()
		{
			var (volume, sdf, mat) = AllocateVolume();
			try
			{
				var bounds = new MinMaxAABB(float3.zero, new float3(CHUNK_SIZE));
				var ltw = float4x4.TRS(new float3(5, 0, 0), quaternion.identity, new float3(1, 1, 1));
				var wtl = math.inverse(ltw);

				var worldCenter = new float3(21, 16, 16); // local (16,16,16) + translation (5,0,0)
				var stamp = new NativeVoxelStampProcedural
				{
					shape = new ProceduralShape
					{
						shape = ProceduralShape.Shape.SPHERE,
						sphere = new ProceduralSphere { center = worldCenter, radius = 3f },
					},
					bounds = new MinMaxAABB(worldCenter - 4, worldCenter + 4),
					strength = 1f,
					material = 9,
				};

				var job = new ApplyVoxelStampJob
				{
					stamp = stamp,
					volumeSdf = sdf,
					volumeMaterials = mat,
					localVolumeBounds = bounds,
					voxelSize = 1f,
					volumeLTW = ltw,
					volumeWTL = wtl,
					sdfScale = 32f,
				};

				job.Schedule().Complete();

				var idx = (16 << X_SHIFT) + (16 << Y_SHIFT) + 16;
				Assert.AreEqual(96, sdf[idx]);
				Assert.AreEqual(9, mat[idx]);
			}
			finally
			{
				volume.Dispose();
			}
		}
	}
}
