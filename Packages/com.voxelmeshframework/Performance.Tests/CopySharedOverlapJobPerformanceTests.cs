namespace Voxels.Performance.Tests
{
	using Core.Meshing;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Jobs;
	using Unity.PerformanceTesting;
	using static Core.VoxelConstants;

	[TestFixture]
	public class CopySharedOverlapJobPerformanceTests
	{
		[Test]
		[Performance]
		public void CopyOverlap_X_Axis()
		{
			RunCopyOverlapPerf(0);
		}

		[Test]
		[Performance]
		public void CopyOverlap_Y_Axis()
		{
			RunCopyOverlapPerf(1);
		}

		[Test]
		[Performance]
		public void CopyOverlap_Z_Axis()
		{
			RunCopyOverlapPerf(2);
		}

		static void RunCopyOverlapPerf(byte axis)
		{
			var srcSdf = new NativeArray<sbyte>(VOLUME_LENGTH, Allocator.TempJob);
			var srcMat = new NativeArray<byte>(VOLUME_LENGTH, Allocator.TempJob);
			var dstSdf = new NativeArray<sbyte>(VOLUME_LENGTH, Allocator.TempJob);
			var dstMat = new NativeArray<byte>(VOLUME_LENGTH, Allocator.TempJob);

			// Fill source with a recognizable pattern
			for (var i = 0; i < VOLUME_LENGTH; i++)
			{
				srcSdf[i] = (sbyte)((i % 251) - 125);
				srcMat[i] = (byte)(1 + (i % 4));
				dstSdf[i] = 0;
				dstMat[i] = 0;
			}

			try
			{
				Measure
					.Method(() =>
					{
						var job = new CopySharedOverlapJob
						{
							sourceSdf = srcSdf,
							sourceMaterials = srcMat,
							destSdf = dstSdf,
							destMaterials = dstMat,
							axis = axis,
						};
						job.Run();
					})
					.WarmupCount(10)
					.IterationsPerMeasurement(50)
					.MeasurementCount(10)
					.GC()
					.Run();
			}
			finally
			{
				srcSdf.Dispose();
				srcMat.Dispose();
				dstSdf.Dispose();
				dstMat.Dispose();
			}
		}
	}
}
