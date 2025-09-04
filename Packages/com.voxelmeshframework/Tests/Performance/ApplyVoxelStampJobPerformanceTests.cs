namespace Voxels.Tests.Editor
{
	using Core.Spatial;
	using Core.Stamps;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using Unity.PerformanceTesting;
	using static Core.VoxelConstants;

	[TestFixture]
	public class ApplyVoxelStampJobPerformanceTests
	{
		[Test]
		[Performance]
		public void ApplyStamp_Sphere_Place()
		{
			RunStampPerfTest(0.75f);
		}

		[Test]
		[Performance]
		public void ApplyStamp_Sphere_Dig()
		{
			RunStampPerfTest(-0.75f);
		}

		static void RunStampPerfTest(float strength)
		{
			var volume = new VoxelVolumeData(Allocator.TempJob) { voxelSize = 1f };

			// Initialize to empty space (outside positive)
			for (var i = 0; i < volume.sdfVolume.Length; i++)
				volume.sdfVolume[i] = 127;

			var center = new float3(16, 16, 16);
			var radius = 8f;
			var stamp = new NativeVoxelStampProcedural
			{
				shape = new ProceduralShape
				{
					shape = ProceduralShape.Shape.SPHERE,
					sphere = new ProceduralSphere { center = center, radius = radius },
				},
				bounds = MinMaxAABB.CreateFromCenterAndExtents(center, radius * 2f),
				strength = strength,
				material = 2,
			};

			var localBounds = new MinMaxAABB(float3.zero, new float3(CHUNK_SIZE));
			const float sdfScale = 32f;

			try
			{
				Measure
					.Method(() =>
					{
						var job = new ApplyVoxelStampJob
						{
							stamp = stamp,
							volumeSdf = volume.sdfVolume,
							volumeMaterials = volume.materials,
							localVolumeBounds = localBounds,
							voxelSize = 1f,
							volumeLTW = float4x4.identity,
							volumeWTL = float4x4.identity,
							sdfScale = sdfScale,
							deltaTime = 1f / 60f,
							alphaPerSecond = 6f,
						};

						job.Run();
					})
					.WarmupCount(5)
					.IterationsPerMeasurement(20)
					.MeasurementCount(10)
					.GC()
					.Run();
			}
			finally
			{
				volume.Dispose();
			}
		}
	}
}
