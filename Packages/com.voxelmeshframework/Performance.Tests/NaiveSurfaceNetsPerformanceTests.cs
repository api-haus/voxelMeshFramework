namespace Voxels.Performance.Tests
{
	using Core;
	using Core.Meshing;
	using Core.Meshing.Algorithms;
	using Core.ThirdParty.SurfaceNets;
	using Core.ThirdParty.SurfaceNets.Utils;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using Unity.PerformanceTesting;

	[TestFixture]
	public class NaiveSurfaceNetsPerformanceTests
	{
		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			SharedStaticMeshingResources.Initialize();
			m_EdgeTable = SharedStaticMeshingResources.EdgeTable;
		}

		NativeArray<ushort> m_EdgeTable;

		[Test]
		[Performance]
		public void SurfaceNets_Sphere_GradientNormals()
		{
			RunSurfaceNetsPerfTest(
				CreateSphereSdf(16f, 16f, 16f, 8f),
				CreateUniformMaterials(1),
				NormalsMode.GRADIENT
			);
		}

		[Test]
		[Performance]
		public void SurfaceNets_Sphere_TriangleNormals()
		{
			RunSurfaceNetsPerfTest(
				CreateSphereSdf(16f, 16f, 16f, 8f),
				CreateUniformMaterials(1),
				NormalsMode.TRIANGLE_GEOMETRY
			);
		}

		[Test]
		[Performance]
		public void SurfaceNets_Plane_GradientNormals()
		{
			RunSurfaceNetsPerfTest(
				CreatePlaneSdf(0f, 1f, 0f, 0f),
				CreateUniformMaterials(1),
				NormalsMode.GRADIENT
			);
		}

		[Test]
		[Performance]
		public void SurfaceNets_Plane_TriangleNormals()
		{
			RunSurfaceNetsPerfTest(
				CreatePlaneSdf(0f, 1f, 0f, 0f),
				CreateUniformMaterials(1),
				NormalsMode.TRIANGLE_GEOMETRY
			);
		}

		void RunSurfaceNetsPerfTest(
			NativeArray<sbyte> volume,
			NativeArray<byte> materials,
			NormalsMode normalsMode
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

			try
			{
				Measure
					.Method(() =>
					{
						vertices.Clear();
						indices.Clear();
						bounds.Item = new MinMaxAABB(float.PositiveInfinity, float.NegativeInfinity);

						var job = new NaiveSurfaceNets
						{
							edgeTable = m_EdgeTable,
							volume = volume,
							materials = materials,
							buffer = buffer,
							indices = indices,
							vertices = vertices,
							bounds = bounds,
							normalsMode = normalsMode,
							voxelSize = 1.0f,
						};

						job.Run();

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

		static NativeArray<sbyte> CreatePlaneSdf(float nx, float ny, float nz, float d)
		{
			var n = math.normalize(new float3(nx, ny, nz));
			var volume = new NativeArray<sbyte>(VoxelConstants.VOLUME_LENGTH, Allocator.TempJob);
			for (var x = 0; x < VoxelConstants.CHUNK_SIZE; x++)
			for (var y = 0; y < VoxelConstants.CHUNK_SIZE; y++)
			for (var z = 0; z < VoxelConstants.CHUNK_SIZE; z++)
			{
				var pos = new float3(x, y, z);
				var distance = math.dot(n, pos) + d;
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
