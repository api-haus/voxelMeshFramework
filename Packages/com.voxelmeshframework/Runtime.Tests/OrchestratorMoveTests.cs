namespace Voxels.Runtime.Tests
{
	using Core.Concurrency;
	using Core.Grids;
	using Core.Meshing.Systems;
	using NUnit.Framework;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using Unity.Transforms;

	[TestFixture]
	public class OrchestratorMoveTests
	{
		[SetUp]
		public void Setup()
		{
			world = new World("TestWorld");
			em = world.EntityManager;
			world.CreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
			world.CreateSystem<VoxelJobFenceRegistrySystem>();
			world.CreateSystem<VoxelMeshingSystem>();
			world.CreateSystemManaged<ManagedVoxelMeshingSystem>();
			world.CreateSystem<RollingGridOrchestratorSystem>();
		}

		[TearDown]
		public void Teardown()
		{
			world.Dispose();
		}

		World world;
		EntityManager em;

		[Test]
		public void BeginMove_EnablesCommitEvent_WhenBatchCompletes()
		{
			var grid = em.CreateEntity(
				typeof(LocalToWorld),
				typeof(NativeVoxelGrid),
				typeof(RollingGridConfig),
				typeof(RollingGridMoveRequest),
				typeof(RollingGridBatchActive)
			);
			// Ensure LinkedEntityGroup exists and contains root
			var leg = em.AddBuffer<LinkedEntityGroup>(grid);
			leg.Add(new LinkedEntityGroup { Value = grid });
			em.SetComponentData(grid, new LocalToWorld { Value = float4x4.identity });
			var cfg = new RollingGridConfig { enabled = true, slotDims = new int3(2, 1, 2) };
			em.SetComponentData(grid, cfg);
			em.SetComponentEnabled<RollingGridBatchActive>(grid, false);
			var bounds = MinMaxAABB.CreateFromCenterAndExtents(float3.zero, new float3(60f));
			em.SetComponentData(
				grid,
				new NativeVoxelGrid
				{
					gridID = 1,
					voxelSize = 1f,
					bounds = bounds,
				}
			);

			// request a +X move by 1 chunk
			var req = new RollingGridMoveRequest { targetAnchorWorldChunk = new int3(1, 0, 0) };
			em.SetComponentData(grid, req);
			em.SetComponentEnabled<RollingGridMoveRequest>(grid, true);

			// Frame 1: schedule batch and write components via ECB
			world.GetOrCreateSystem<RollingGridOrchestratorSystem>().Update(world.Unmanaged);
			world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>().Update();
			// Ensure commit event component exists disabled (mirrors orchestrator behavior)
			if (!em.HasComponent<RollingGridCommitEvent>(grid))
				em.AddComponent<RollingGridCommitEvent>(grid);
			em.SetComponentEnabled<RollingGridCommitEvent>(grid, false);
			// Simulate batch completion fence
			VoxelJobFenceRegistry.CompleteAndReset(grid);
			// Poll for a few frames to allow ready path to enable commit
			for (var i = 0; i < 3; i++)
			{
				world.GetOrCreateSystem<RollingGridOrchestratorSystem>().Update(world.Unmanaged);
				world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>().Update();
			}

			// Commit event should be enabled
			Assert.IsTrue(
				em.HasComponent<RollingGridCommitEvent>(grid)
					&& em.IsComponentEnabled<RollingGridCommitEvent>(grid)
			);
		}
	}
}
