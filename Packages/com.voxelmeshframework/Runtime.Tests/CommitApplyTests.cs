namespace Voxels.Runtime.Tests
{
	using Core.Grids;
	using Core.Meshing.Systems;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using Unity.Transforms;

	[TestFixture]
	public class CommitApplyTests
	{
		[SetUp]
		public void Setup()
		{
			world = new World("TestWorld");
			em = world.EntityManager;
			world.CreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
			world.CreateSystemManaged<ManagedVoxelMeshingSystem>();
		}

		[TearDown]
		public void Teardown()
		{
			world.Dispose();
		}

		World world;
		EntityManager em;

		[Test]
		public void Commit_AppliesMeshesAndDisablesFlags()
		{
			// Grid root with commit event enabled
			var grid = em.CreateEntity(typeof(LocalToWorld), typeof(NativeVoxelGrid));
			em.SetComponentData(
				grid,
				new NativeVoxelGrid
				{
					gridID = 7,
					voxelSize = 1f,
					bounds = MinMaxAABB.CreateFromCenterAndExtents(float3.zero, new float3(1f)),
				}
			);
			var leg = em.AddBuffer<LinkedEntityGroup>(grid);
			leg.Add(new LinkedEntityGroup { Value = grid });

			// Chunk entity belonging to the grid with preallocated meshData and NeedsManagedMeshUpdate
			var chunk = em.CreateEntity(
				typeof(NativeVoxelChunk),
				typeof(Core.Meshing.NativeVoxelMesh),
				typeof(Core.Meshing.Tags.NeedsManagedMeshUpdate)
			);
			em.SetComponentEnabled<Core.Meshing.Tags.NeedsManagedMeshUpdate>(chunk, true);
			em.SetComponentData(chunk, new NativeVoxelChunk { coord = int3.zero, gridID = 7 });
			leg.Add(new LinkedEntityGroup { Value = chunk });

			// Allocate NativeVoxelMesh and writable mesh data to simulate staged mesh ready for apply
			var nvm = new Core.Meshing.NativeVoxelMesh(Allocator.Persistent);
			nvm.meshing.meshData = UnityEngine.Mesh.AllocateWritableMeshData(1);
			em.SetComponentData(chunk, nvm);

			// Enable commit event on grid
			if (!em.HasComponent<RollingGridCommitEvent>(grid))
				em.AddComponent<RollingGridCommitEvent>(grid);
			em.SetComponentData(
				grid,
				new RollingGridCommitEvent
				{
					gridID = 7,
					targetAnchorWorldChunk = int3.zero,
					targetOriginWorld = float3.zero,
				}
			);
			em.SetComponentEnabled<RollingGridCommitEvent>(grid, true);

			// Run managed apply system and play back ECB
			world.GetOrCreateSystemManaged<ManagedVoxelMeshingSystem>().Update();
			world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>().Update();

			// Assert mesh applied (meshData cleared) and flag disabled
			var nvmPost = em.GetComponentData<Core.Meshing.NativeVoxelMesh>(chunk);
			Assert.Zero(nvmPost.meshing.meshData.Length, "meshData was not applied and disposed");
			Assert.IsFalse(
				em.IsComponentEnabled<Core.Meshing.Tags.NeedsManagedMeshUpdate>(chunk),
				"NeedsManagedMeshUpdate should be disabled after apply"
			);
			// Commit event should be disabled after processing
			Assert.IsFalse(
				em.IsComponentEnabled<RollingGridCommitEvent>(grid),
				"Commit event should be disabled after commit processing"
			);
		}
	}
}
