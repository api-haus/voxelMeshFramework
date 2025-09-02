namespace Voxels.Tests.Editor
{
	using Core.Meshing;
	using Core.ThirdParty.SurfaceNets;
	using Core.ThirdParty.SurfaceNets.Utils;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.PerformanceTesting;
	using UnityEngine;
	using static Core.VoxelConstants;

	[TestFixture]
	public class NaiveSurfaceNetsPerformanceTests
	{
		NativeArray<ushort> m_EdgeTable;

		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			if (!SharedStaticMeshingResources.EdgeTable.IsCreated)
				SharedStaticMeshingResources.Init();
			m_EdgeTable = SharedStaticMeshingResources.EdgeTable;
		}

		[Test, Performance]
		public void SurfaceNets_Sphere_RecalcNormals_Off()
		{
			RunSurfaceNetsPerfTest(CreateSphereSdf(16f, 16f, 16f, 8f), CreateUniformMaterials(1), false);
		}

		[Test, Performance]
		public void SurfaceNets_Sphere_RecalcNormals_On()
		{
			RunSurfaceNetsPerfTest(CreateSphereSdf(16f, 16f, 16f, 8f), CreateUniformMaterials(1), true);
		}

		[Test, Performance]
		public void SurfaceNets_Plane_RecalcNormals_Off()
		{
			RunSurfaceNetsPerfTest(CreatePlaneSdf(0f, 1f, 0f, 0f), CreateUniformMaterials(1), false);
		}

		[Test, Performance]
		public void SurfaceNets_Plane_RecalcNormals_On()
		{
			RunSurfaceNetsPerfTest(CreatePlaneSdf(0f, 1f, 0f, 0f), CreateUniformMaterials(1), true);
		}

		void RunSurfaceNetsPerfTest(
			NativeArray<sbyte> volume,
			NativeArray<byte> materials,
			bool recalcNormals
		)
		{
			var buffer = new NativeArray<int>(
				(CHUNK_SIZE + 1) * (CHUNK_SIZE + 1) * (CHUNK_SIZE + 1),
				Allocator.TempJob
			);
			var vertices = new NativeList<Vertex>(Allocator.TempJob);
			var indices = new NativeList<int>(Allocator.TempJob);
			var bounds = UnsafePointer<Unity.Mathematics.Geometry.MinMaxAABB>.Create();

			try
			{
				Measure
					.Method(() =>
					{
						vertices.Clear();
						indices.Clear();
						bounds.Item = new Unity.Mathematics.Geometry.MinMaxAABB(
							float.PositiveInfinity,
							float.NegativeInfinity
						);

						var job = new NaiveSurfaceNets
						{
							edgeTable = m_EdgeTable,
							volume = volume,
							materials = materials,
							buffer = buffer,
							indices = indices,
							vertices = vertices,
							bounds = bounds,
							recalculateNormals = recalcNormals,
							voxelSize = 1.0f,
						};

						job.Run();

						Measure.Custom(
							new SampleGroup("Vertices", SampleUnit.Undefined, false),
							vertices.Length
						);
						Measure.Custom(
							new SampleGroup("Triangles", SampleUnit.Undefined, false),
							indices.Length / 3f
						);
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
			var volume = new NativeArray<sbyte>(VOLUME_LENGTH, Allocator.TempJob);
			for (var x = 0; x < CHUNK_SIZE; x++)
			for (var y = 0; y < CHUNK_SIZE; y++)
			for (var z = 0; z < CHUNK_SIZE; z++)
			{
				var pos = new float3(x, y, z);
				var distance = math.length(pos - center) - radius;
				distance = math.clamp(distance, -127, 127);
				var index = (x * CHUNK_SIZE * CHUNK_SIZE) + (y * CHUNK_SIZE) + z;
				volume[index] = (sbyte)distance;
			}
			return volume;
		}

		static NativeArray<sbyte> CreatePlaneSdf(float nx, float ny, float nz, float d)
		{
			var n = math.normalize(new float3(nx, ny, nz));
			var volume = new NativeArray<sbyte>(VOLUME_LENGTH, Allocator.TempJob);
			for (var x = 0; x < CHUNK_SIZE; x++)
			for (var y = 0; y < CHUNK_SIZE; y++)
			for (var z = 0; z < CHUNK_SIZE; z++)
			{
				var pos = new float3(x, y, z);
				var distance = math.dot(n, pos) + d;
				distance = math.clamp(distance, -127, 127);
				var index = (x * CHUNK_SIZE * CHUNK_SIZE) + (y * CHUNK_SIZE) + z;
				volume[index] = (sbyte)distance;
			}
			return volume;
		}

		static NativeArray<byte> CreateUniformMaterials(byte materialId)
		{
			var materials = new NativeArray<byte>(VOLUME_LENGTH, Allocator.TempJob);
			for (var i = 0; i < VOLUME_LENGTH; i++)
				materials[i] = materialId;
			return materials;
		}
	}
}
