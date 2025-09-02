namespace Voxels.Tests.Editor
{
	using Core.Concurrency;
	using Core.Meshing;
	using Core.ThirdParty.SurfaceNets;
	using Core.ThirdParty.SurfaceNets.Utils;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using static Core.VoxelConstants;

	[TestFixture]
	public class NaiveSurfaceNetsTests
	{
		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			SharedStaticMeshingResources.Initialize();
			VoxelJobFenceRegistry.Initialize();

			m_EdgeTable = SharedStaticMeshingResources.EdgeTable;
		}

		[OneTimeTearDown]
		public void OneTimeTearDown()
		{
			// No need to dispose SharedStaticMeshingResources
		}

		NativeArray<ushort> m_EdgeTable;

		[Test]
		public void NaiveSurfaceNets_EmptyVolume_ProducesNoMesh()
		{
			// Arrange
			var volume = new NativeArray<sbyte>(VOLUME_LENGTH, Allocator.TempJob);
			var materials = new NativeArray<byte>(VOLUME_LENGTH, Allocator.TempJob);
			var buffer = new NativeArray<int>(
				(CHUNK_SIZE + 1) * (CHUNK_SIZE + 1) * (CHUNK_SIZE + 1),
				Allocator.TempJob
			);
			var vertices = new NativeList<Vertex>(Allocator.TempJob);
			var indices = new NativeList<int>(Allocator.TempJob);
			var bounds = UnsafePointer<MinMaxAABB>.Create();

			// Initialize volume with all positive values (empty space)
			for (var i = 0; i < volume.Length; i++)
			{
				volume[i] = 127; // Maximum positive value
				materials[i] = 1; // Default material
			}

			// Act
			var job = new NaiveSurfaceNets
			{
				edgeTable = m_EdgeTable,
				volume = volume,
				materials = materials,
				buffer = buffer,
				indices = indices,
				vertices = vertices,
				bounds = bounds,
				recalculateNormals = false,
				voxelSize = 1.0f,
			};

			job.Schedule().Complete();

			// Assert
			Assert.AreEqual(0, vertices.Length, "Empty volume should produce no vertices");
			Assert.AreEqual(0, indices.Length, "Empty volume should produce no indices");

			// Cleanup
			volume.Dispose();
			materials.Dispose();
			buffer.Dispose();
			vertices.Dispose();
			indices.Dispose();
			bounds.Dispose();
		}

		[Test]
		public void NaiveSurfaceNets_SimpleSphere_ProducesMesh()
		{
			// Arrange
			var volume = new NativeArray<sbyte>(VOLUME_LENGTH, Allocator.TempJob);
			var materials = new NativeArray<byte>(VOLUME_LENGTH, Allocator.TempJob);
			var buffer = new NativeArray<int>(
				(CHUNK_SIZE + 1) * (CHUNK_SIZE + 1) * (CHUNK_SIZE + 1),
				Allocator.TempJob
			);
			var vertices = new NativeList<Vertex>(Allocator.TempJob);
			var indices = new NativeList<int>(Allocator.TempJob);
			var bounds = UnsafePointer<MinMaxAABB>.Create();

			// Create a simple sphere in the center of the volume
			var center = new float3(16, 16, 16);
			var radius = 8.0f;

			for (var x = 0; x < CHUNK_SIZE; x++)
			for (var y = 0; y < CHUNK_SIZE; y++)
			for (var z = 0; z < CHUNK_SIZE; z++)
			{
				var pos = new float3(x, y, z);
				var distance = math.length(pos - center) - radius;

				// Clamp to sbyte range
				distance = math.clamp(distance, -127, 127);

				var index = (x * CHUNK_SIZE * CHUNK_SIZE) + (y * CHUNK_SIZE) + z;
				volume[index] = (sbyte)distance;
				materials[index] = 1; // Default material
			}

			// Act
			var job = new NaiveSurfaceNets
			{
				edgeTable = m_EdgeTable,
				volume = volume,
				materials = materials,
				buffer = buffer,
				indices = indices,
				vertices = vertices,
				bounds = bounds,
				recalculateNormals = true,
				voxelSize = 1.0f,
			};

			job.Schedule().Complete();

			// Assert
			Assert.Greater(vertices.Length, 0, "Sphere should produce vertices");
			Assert.Greater(indices.Length, 0, "Sphere should produce indices");
			Assert.AreEqual(0, indices.Length % 3, "Indices should be a multiple of 3 (triangles)");

			// Verify bounds make sense
			var meshBounds = bounds.Item;
			Assert.IsTrue(meshBounds.IsValid, "Bounds should be valid");

			// Verify all vertices have normalized normals
			for (var i = 0; i < vertices.Length; i++)
			{
				var normal = vertices[i].normal;
				var length = math.length(normal);
				Assert.AreEqual(1.0f, length, 0.01f, $"Vertex {i} normal should be normalized");
			}

			// Cleanup
			volume.Dispose();
			materials.Dispose();
			buffer.Dispose();
			vertices.Dispose();
			indices.Dispose();
			bounds.Dispose();
		}

		[Test]
		public void NaiveSurfaceNets_VoxelSizeScaling_WorksCorrectly()
		{
			// Arrange
			var volume = new NativeArray<sbyte>(VOLUME_LENGTH, Allocator.TempJob);
			var materials = new NativeArray<byte>(VOLUME_LENGTH, Allocator.TempJob);
			var buffer = new NativeArray<int>(
				(CHUNK_SIZE + 1) * (CHUNK_SIZE + 1) * (CHUNK_SIZE + 1),
				Allocator.TempJob
			);
			var vertices1 = new NativeList<Vertex>(Allocator.TempJob);
			var indices1 = new NativeList<int>(Allocator.TempJob);
			var bounds1 = UnsafePointer<MinMaxAABB>.Create();
			var vertices2 = new NativeList<Vertex>(Allocator.TempJob);
			var indices2 = new NativeList<int>(Allocator.TempJob);
			var bounds2 = UnsafePointer<MinMaxAABB>.Create();

			// Initialize all to positive (outside)
			for (var i = 0; i < VOLUME_LENGTH; i++)
			{
				volume[i] = 127; // Outside
				materials[i] = 1; // Default material
			}

			// Create a simple cube
			for (var x = 8; x < 24; x++)
			for (var y = 8; y < 24; y++)
			for (var z = 8; z < 24; z++)
			{
				var index = (x * CHUNK_SIZE * CHUNK_SIZE) + (y * CHUNK_SIZE) + z;
				volume[index] = -127; // Inside
			}

			// Act - Generate mesh with voxel size 1.0
			var job1 = new NaiveSurfaceNets
			{
				edgeTable = m_EdgeTable,
				volume = volume,
				materials = materials,
				buffer = buffer,
				indices = indices1,
				vertices = vertices1,
				bounds = bounds1,
				recalculateNormals = false,
				voxelSize = 1.0f,
			};
			job1.Schedule().Complete();

			// Reset buffer
			for (var i = 0; i < buffer.Length; i++)
				buffer[i] = 0;

			// Act - Generate mesh with voxel size 2.0
			var job2 = new NaiveSurfaceNets
			{
				edgeTable = m_EdgeTable,
				volume = volume,
				materials = materials,
				buffer = buffer,
				indices = indices2,
				vertices = vertices2,
				bounds = bounds2,
				recalculateNormals = false,
				voxelSize = 2.0f,
			};
			job2.Schedule().Complete();

			// Assert
			Assert.AreEqual(
				vertices1.Length,
				vertices2.Length,
				"Same volume should produce same vertex count"
			);

			// Check that all vertices are scaled correctly
			for (var i = 0; i < vertices1.Length; i++)
			{
				var pos1 = vertices1[i].position;
				var pos2 = vertices2[i].position;

				// Position should be scaled by voxel size
				Assert.AreEqual(pos1.x * 2.0f, pos2.x, 0.001f, "X position should be scaled");
				Assert.AreEqual(pos1.y * 2.0f, pos2.y, 0.001f, "Y position should be scaled");
				Assert.AreEqual(pos1.z * 2.0f, pos2.z, 0.001f, "Z position should be scaled");
			}

			// Cleanup
			volume.Dispose();
			materials.Dispose();
			buffer.Dispose();
			vertices1.Dispose();
			indices1.Dispose();
			bounds1.Dispose();
			vertices2.Dispose();
			indices2.Dispose();
			bounds2.Dispose();
		}
	}
}
