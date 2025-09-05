namespace Voxels.Runtime.Tests
{
	using System.Collections;
	using System.Reflection;
	using Core.Authoring;
	using Core.Concurrency;
	using Core.Grids;
	using Core.Procedural;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;
	using UnityEngine;
	using UnityEngine.TestTools;

	public class RollingGridPlayerDriverTests
	{
		World previous;
		World world;
		EntityManager em;

		[SetUp]
		public void Setup()
		{
			previous = World.DefaultGameObjectInjectionWorld;
			world = new World("RGDriverWorld");
			World.DefaultGameObjectInjectionWorld = world;
			em = world.EntityManager;
			world.CreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
			world.CreateSystemManaged<EndInitializationEntityCommandBufferSystem>();
			world.CreateSystem<Voxels.Core.Concurrency.VoxelJobFenceRegistrySystem>();
			world.CreateSystem<Voxels.Core.Meshing.Systems.VoxelMeshAllocationSystem>();
			world.CreateSystem<Voxels.Core.Meshing.Systems.RollingGridOrchestratorSystem>();
			world.CreateSystemManaged<Voxels.Core.Meshing.Systems.ManagedVoxelMeshingSystem>();
			world.CreateSystemManaged<Voxels.Core.Hybrid.EntityGameObjectTransformSystem>();
		}

		[TearDown]
		public void Teardown()
		{
			world.Dispose();
			World.DefaultGameObjectInjectionWorld = previous;
		}

		[UnityTest]
		public IEnumerator Sends_MoveRequests_OnChunkBoundary_SingleAxis()
		{
			var gridGO = new GameObject("Grid");
			gridGO.SetActive(false);
			var grid = gridGO.AddComponent<VoxelMeshGrid>();
			// set serialized internals before Awake by keeping GO inactive
			grid.__SetRolling(true); // Must be true for rolling grid tests
			grid.__SetVoxelSize(1f);
			grid.__SetWorldBounds(new Bounds(Vector3.zero, new Vector3(64f, 32f, 64f)));
			grid.__SetExternalDriver(true);
			var gen = gridGO.AddComponent<HalfSphereVoxelGenerator>();
			grid.__SetProcedural(gen);
			gridGO.SetActive(true);

			var playerGO = new GameObject("Player");
			playerGO.SetActive(false);
			var driver = playerGO.AddComponent<RollingGridPlayerDriver>();
			// assign via internal test hook
			driver.__SetGrid(grid);
			playerGO.SetActive(true);

			// allow Awake to run and entity to be created
			yield return null;

			// initial position at origin should send (0,0,0)
			playerGO.transform.position = Vector3.zero;
			yield return null;

			var e = FindGridEntityById(grid.gameObject.GetInstanceID());
			Assert.AreNotEqual(Entity.Null, e);
			Assert.True(em.HasComponent<RollingGridMoveRequest>(e));
			var req0 = em.GetComponentData<RollingGridMoveRequest>(e);
			Assert.AreEqual(new int3(0, 0, 0), req0.targetAnchorWorldChunk);

			// move diagonally by more than one stride -> driver now sends full target, orchestrator handles progressive movement
			playerGO.transform.position = new Vector3(33f, 33f, 0f);
			yield return null;
			// ensure job fences don't block this test
			var ge = FindGridEntityById(grid.gameObject.GetInstanceID());
			VoxelJobFenceRegistry.Initialize();
			VoxelJobFenceRegistry.CompleteAndReset(ge);

			var req1 = em.GetComponentData<RollingGridMoveRequest>(e);
			// Current behavior: driver sends full target immediately, orchestrator handles single-axis progression
			Assert.AreEqual(new int3(1, 1, 0), req1.targetAnchorWorldChunk);

			// advance another frame at same position -> should remain same since driver only sends when target changes
			yield return null;
			var req2 = em.GetComponentData<RollingGridMoveRequest>(e);
			Assert.AreEqual(new int3(1, 1, 0), req2.targetAnchorWorldChunk);

			Object.DestroyImmediate(playerGO);
			Object.DestroyImmediate(gridGO);
		}

		Entity FindGridEntityById(int instanceId)
		{
			using var arr = em.CreateEntityQuery(ComponentType.ReadOnly<NativeVoxelGrid>())
				.ToEntityArray(Allocator.Temp);
			for (var i = 0; i < arr.Length; i++)
			{
				var e = arr[i];
				var g = em.GetComponentData<NativeVoxelGrid>(e);
				if (g.gridID == instanceId)
					return e;
			}
			return Entity.Null;
		}
	}
}
