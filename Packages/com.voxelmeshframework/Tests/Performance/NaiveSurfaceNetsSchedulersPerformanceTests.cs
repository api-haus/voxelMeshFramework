namespace Voxels.Tests.Editor
{
	using Core;
	using Core.Meshing;
	using Core.ThirdParty.SurfaceNets;
	using Core.ThirdParty.SurfaceNets.Utils;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using Unity.PerformanceTesting;

	[TestFixture]
	public class NaiveSurfaceNetsSchedulersPerformanceTests
	{
		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			if (!SharedStaticMeshingResources.EdgeTable.IsCreated)
				SharedStaticMeshingResources.Initialize();
			m_EdgeTable = SharedStaticMeshingResources.EdgeTable;
		}

		NativeArray<ushort> m_EdgeTable;

		[Test]
		[Performance]
		public void Scheduler_NaiveSurfaceNets_Sphere_GradientNormals()
		{
			RunSchedulerPerfTest(
				CreateSphereSdf(16f, 16f, 16f, 8f),
				CreateUniformMaterials(1),
				NormalsMode.GRADIENT,
				false
			);
		}

		[Test]
		[Performance]
		public void Scheduler_NaiveSurfaceNetsFairing_Sphere_WithIterations()
		{
			RunSchedulerPerfTest(
				CreateSphereSdf(16f, 16f, 16f, 8f),
				CreateUniformMaterials(1),
				NormalsMode.GRADIENT,
				true
			);
		}

		void RunSchedulerPerfTest(
			NativeArray<sbyte> volume,
			NativeArray<byte> materials,
			NormalsMode normalsMode,
			bool useFairing
		)
		{
			var buffer = new NativeArray<int>(
				(VoxelConstants.CHUNK_SIZE + 1)
					* (VoxelConstants.CHUNK_SIZE + 1)
					* (VoxelConstants.CHUNK_SIZE + 1),
				Allocator.TempJob
			);
			var vertices = new NativeList<Vertex>(Allocator.TempJob);
			var indices = new NativeList<int>(Allocator.TempJob);
			var bounds = UnsafePointer<MinMaxAABB>.Create();

			var input = new MeshingInputData
			{
				volume = volume,
				materials = materials,
				edgeTable = m_EdgeTable,
				voxelSize = 1.0f,
				chunkSize = VoxelConstants.CHUNK_SIZE,
				normalsMode = normalsMode,
				materialDistributionMode = MaterialDistributionMode.BLENDED_CORNER_SUM,
				copyApronPostMesh = false,
			};

			var output = new MeshingOutputData
			{
				vertices = vertices,
				indices = indices,
				buffer = buffer,
				bounds = bounds,
			};

			FairingBuffers fairingBuffers = default;
			if (useFairing)
				fairingBuffers = new FairingBuffers(Allocator.TempJob);

			try
			{
				Measure
					.Method(() =>
					{
						vertices.Clear();
						indices.Clear();
						bounds.Item = new MinMaxAABB(float.PositiveInfinity, float.NegativeInfinity);

						JobHandle handle;
						if (!useFairing)
							handle = new NaiveSurfaceNetsScheduler().Schedule(input, output, default);
						else
							handle = new NaiveSurfaceNetsFairingScheduler
							{
								fairingBuffers = fairingBuffers,
								fairingIterations = 5,
								fairingStepSize = 0.6f,
								cellMargin = 0.1f,
								recomputeNormalsAfterFairing = false,
							}.Schedule(input, output, default);

						handle.Complete();

						Measure.Custom(new SampleGroup("Vertices", SampleUnit.Undefined), vertices.Length);
						Measure.Custom(new SampleGroup("Triangles", SampleUnit.Undefined), indices.Length / 3f);
					})
					.WarmupCount(5)
					.IterationsPerMeasurement(10)
					.MeasurementCount(10)
					.GC()
					.Run();
			}
			finally
			{
				volume.Dispose();
				materials.Dispose();
				buffer.Dispose();
				vertices.Dispose();
				indices.Dispose();
				bounds.Dispose();
				if (useFairing)
					fairingBuffers.Dispose();
			}
		}

		static NativeArray<sbyte> CreateSphereSdf(float cx, float cy, float cz, float radius)
		{
			var center = new float3(cx, cy, cz);
			var volume = new NativeArray<sbyte>(VoxelConstants.VOLUME_LENGTH, Allocator.TempJob);
			for (var x = 0; x < VoxelConstants.CHUNK_SIZE; x++)
			for (var y = 0; y < VoxelConstants.CHUNK_SIZE; y++)
			for (var z = 0; z < VoxelConstants.CHUNK_SIZE; z++)
			{
				var pos = new float3(x, y, z);
				var distance = math.length(pos - center) - radius;
				distance = math.clamp(distance, -127, 127);
				var index =
					(x * VoxelConstants.CHUNK_SIZE * VoxelConstants.CHUNK_SIZE)
					+ (y * VoxelConstants.CHUNK_SIZE)
					+ z;
				volume[index] = (sbyte)distance;
			}

			return volume;
		}

		static NativeArray<byte> CreateUniformMaterials(byte materialId)
		{
			var materials = new NativeArray<byte>(VoxelConstants.VOLUME_LENGTH, Allocator.TempJob);
			for (var i = 0; i < VoxelConstants.VOLUME_LENGTH; i++)
				materials[i] = materialId;
			return materials;
		}
	}
}
