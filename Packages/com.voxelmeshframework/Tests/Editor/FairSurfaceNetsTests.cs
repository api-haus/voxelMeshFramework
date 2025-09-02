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
	public class FairSurfaceNetsTests
	{
		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			// In editor tests, we need to ensure the edge table is initialized
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
		public void FairSurfaceNets_WithMaterials_ProducesMeshWithMaterialInfo()
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
			var vertexCellCoords = new NativeList<int3>(Allocator.TempJob);
			var bounds = UnsafePointer<MinMaxAABB>.Create();

			// Create a sphere with two different materials
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

				// Set materials - use material 1 for upper half, material 2 for lower half
				materials[index] = (byte)(y > 16 ? 1 : 2);
			}

			// Act
			var job = new FairSurfaceNets
			{
				edgeTable = m_EdgeTable,
				volume = volume,
				materials = materials,
				buffer = buffer,
				indices = indices,
				vertices = vertices,
				vertexCellCoords = vertexCellCoords,
				bounds = bounds,
				recalculateNormals = true,
				voxelSize = 1.0f,
				enableSurfaceFairing = false, // Test without fairing first
				fairingIterations = 0,
				fairingStepSize = 0.6f,
				cellMargin = 0.1f,
			};

			job.Schedule().Complete();

			// Assert
			Assert.Greater(vertices.Length, 0, "Should produce vertices");
			Assert.Greater(indices.Length, 0, "Should produce indices");

			// Check that vertices have material information
			var hasMaterial1 = false;
			var hasMaterial2 = false;

			for (var i = 0; i < vertices.Length; i++)
			{
				var color = vertices[i].color;
				if (color.r == 1)
					hasMaterial1 = true;
				if (color.r == 2)
					hasMaterial2 = true;
			}

			Assert.IsTrue(hasMaterial1, "Should have vertices with material 1");
			Assert.IsTrue(hasMaterial2, "Should have vertices with material 2");

			// Cleanup
			volume.Dispose();
			materials.Dispose();
			buffer.Dispose();
			vertices.Dispose();
			indices.Dispose();
			vertexCellCoords.Dispose();
			bounds.Dispose();
		}

		[Test]
		public void FairSurfaceNets_WithFairing_ProducesSmoother()
		{
			// Arrange
			var volume = new NativeArray<sbyte>(VOLUME_LENGTH, Allocator.TempJob);
			var materials = new NativeArray<byte>(VOLUME_LENGTH, Allocator.TempJob);
			var buffer = new NativeArray<int>(
				(CHUNK_SIZE + 1) * (CHUNK_SIZE + 1) * (CHUNK_SIZE + 1),
				Allocator.TempJob
			);
			var verticesNoFairing = new NativeList<Vertex>(Allocator.TempJob);
			var verticesWithFairing = new NativeList<Vertex>(Allocator.TempJob);
			var indices1 = new NativeList<int>(Allocator.TempJob);
			var indices2 = new NativeList<int>(Allocator.TempJob);
			var vertexCellCoords1 = new NativeList<int3>(Allocator.TempJob);
			var vertexCellCoords2 = new NativeList<int3>(Allocator.TempJob);
			var bounds1 = UnsafePointer<MinMaxAABB>.Create();
			var bounds2 = UnsafePointer<MinMaxAABB>.Create();

			// Create a simple cube
			for (var x = 10; x < 22; x++)
			for (var y = 10; y < 22; y++)
			for (var z = 10; z < 22; z++)
			{
				var index = (x * CHUNK_SIZE * CHUNK_SIZE) + (y * CHUNK_SIZE) + z;
				volume[index] = -127; // Inside
				materials[index] = 1;
			}

			// Act - Generate without fairing
			var job1 = new FairSurfaceNets
			{
				edgeTable = m_EdgeTable,
				volume = volume,
				materials = materials,
				buffer = buffer,
				indices = indices1,
				vertices = verticesNoFairing,
				vertexCellCoords = vertexCellCoords1,
				bounds = bounds1,
				recalculateNormals = false,
				voxelSize = 1.0f,
				enableSurfaceFairing = false,
				fairingIterations = 0,
				fairingStepSize = 0.6f,
				cellMargin = 0.1f,
			};
			job1.Schedule().Complete();

			// Reset buffer
			for (var i = 0; i < buffer.Length; i++)
				buffer[i] = 0;

			// Act - Generate with fairing
			var job2 = new FairSurfaceNets
			{
				edgeTable = m_EdgeTable,
				volume = volume,
				materials = materials,
				buffer = buffer,
				indices = indices2,
				vertices = verticesWithFairing,
				vertexCellCoords = vertexCellCoords2,
				bounds = bounds2,
				recalculateNormals = false,
				voxelSize = 1.0f,
				enableSurfaceFairing = true,
				fairingIterations = 5,
				fairingStepSize = 0.6f,
				cellMargin = 0.1f,
			};
			job2.Schedule().Complete();

			// Assert
			Assert.AreEqual(
				verticesNoFairing.Length,
				verticesWithFairing.Length,
				"Same volume should produce same vertex count"
			);

			// Calculate average position change
			var totalDisplacement = 0f;
			var displacedVertices = 0;

			for (var i = 0; i < verticesNoFairing.Length; i++)
			{
				var pos1 = verticesNoFairing[i].position;
				var pos2 = verticesWithFairing[i].position;
				var displacement = math.length(pos2 - pos1);

				if (displacement > 0.001f)
				{
					totalDisplacement += displacement;
					displacedVertices++;
				}
			}

			Assert.Greater(displacedVertices, 0, "Fairing should move some vertices");
			Assert.Greater(totalDisplacement, 0f, "Fairing should cause displacement");

			// Fairing should keep vertices within reasonable bounds
			var avgDisplacement = totalDisplacement / displacedVertices;
			Assert.Less(avgDisplacement, 1.0f, "Average displacement should be reasonable");

			// Cleanup
			volume.Dispose();
			materials.Dispose();
			buffer.Dispose();
			verticesNoFairing.Dispose();
			verticesWithFairing.Dispose();
			indices1.Dispose();
			indices2.Dispose();
			vertexCellCoords1.Dispose();
			vertexCellCoords2.Dispose();
			bounds1.Dispose();
			bounds2.Dispose();
		}

		[Test]
		public void FairSurfaceNets_Scheduler_WorksCorrectly()
		{
			// Arrange
			var input = new MeshingInputData
			{
				volume = new NativeArray<sbyte>(VOLUME_LENGTH, Allocator.TempJob),
				materials = new NativeArray<byte>(VOLUME_LENGTH, Allocator.TempJob),
				edgeTable = m_EdgeTable,
				voxelSize = 1.0f,
				chunkSize = CHUNK_SIZE,
				recalculateNormals = true,
			};

			var output = new MeshingOutputData
			{
				vertices = new NativeList<Vertex>(Allocator.TempJob),
				indices = new NativeList<int>(Allocator.TempJob),
				buffer = new NativeArray<int>(
					(CHUNK_SIZE + 1) * (CHUNK_SIZE + 1) * (CHUNK_SIZE + 1),
					Allocator.TempJob
				),
				bounds = UnsafePointer<MinMaxAABB>.Create(),
			};

			// Create a simple sphere
			var center = new float3(16, 16, 16);
			var radius = 8.0f;

			for (var i = 0; i < VOLUME_LENGTH; i++)
			{
				var x = i / (CHUNK_SIZE * CHUNK_SIZE);
				var y = i / CHUNK_SIZE % CHUNK_SIZE;
				var z = i % CHUNK_SIZE;

				var pos = new float3(x, y, z);
				var distance = math.length(pos - center) - radius;

				input.volume[i] = (sbyte)math.clamp(distance, -127, 127);
				input.materials[i] = 1;
			}

			// Act
			var scheduler = new FairSurfaceNetsScheduler
			{
				fairingIterations = 3,
				fairingStepSize = 0.6f,
				cellMargin = 0.1f,
			};

			var handle = scheduler.Schedule(in input, in output, default);
			handle.Complete();

			// Assert
			Assert.Greater(output.vertices.Length, 0, "Scheduler should produce vertices");
			Assert.Greater(output.indices.Length, 0, "Scheduler should produce indices");

			// Cleanup
			input.volume.Dispose();
			input.materials.Dispose();
			output.vertices.Dispose();
			output.indices.Dispose();
			output.buffer.Dispose();
			output.bounds.Dispose();
		}
	}
}
