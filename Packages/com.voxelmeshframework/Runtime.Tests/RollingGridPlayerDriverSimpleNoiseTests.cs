namespace Voxels.Runtime.Tests
{
	using System.Collections;
	using Core.Authoring;
	using Core.Concurrency;
	using Core.Grids;
	using Core.Procedural;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Entities;
	using UnityEngine;
	using UnityEngine.TestTools;

	public class RollingGridPlayerDriverSimpleNoiseTests
	{
		[UnityTest]
		public IEnumerator Driver_Move_Grid_With_SimpleNoise_Forward()
		{
			// Fresh world with required systems
			var prev = World.DefaultGameObjectInjectionWorld;
			var world = new World("RGDriverSNWorld");
			World.DefaultGameObjectInjectionWorld = world;
			var em = world.EntityManager;
			world.CreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
			world.CreateSystem<VoxelJobFenceRegistrySystem>();
			var orchestrator =
				world.CreateSystem<Voxels.Core.Meshing.Systems.RollingGridOrchestratorSystem>();
			var managed =
				world.CreateSystemManaged<Voxels.Core.Meshing.Systems.ManagedVoxelMeshingSystem>();
			var trsSync = world.CreateSystemManaged<Voxels.Core.Hybrid.EntityGameObjectTransformSystem>();

			// Grid host with SimpleNoise generator
			var gridGO = new GameObject("GridHost_SN");
			gridGO.SetActive(false);
			var grid = gridGO.AddComponent<VoxelMeshGrid>();
			grid.__SetRolling(true);
			grid.__SetExternalDriver(true);
			grid.__SetVoxelSize(1f);
			grid.__SetWorldBounds(new Bounds(Vector3.zero, new Vector3(120f, 30f, 120f)));
			var gen = gridGO.AddComponent<SimpleNoiseVoxelGenerator>();
			grid.__SetProcedural(gen);
			gridGO.SetActive(true);

			// Player with driver
			var playerGO = new GameObject("Player_SN");
			playerGO.SetActive(false);
			var driver = playerGO.AddComponent<RollingGridPlayerDriver>();
			driver.__SetGrid(grid);
			playerGO.SetActive(true);

			// Allow Awake and authoring conversion
			yield return null;

			// Disable chunk allocation to focus test purely on grid motion
			var gridEntity = FindGridEntityById(em, gridGO.GetInstanceID());
			Assert.AreNotEqual(Entity.Null, gridEntity);
			if (em.HasComponent<NeedsChunkAllocation>(gridEntity))
				em.SetComponentEnabled<NeedsChunkAllocation>(gridEntity, false);

			// Move player forward (+Z) by > stride
			var stride = Voxels.Core.VoxelConstants.EFFECTIVE_CHUNK_SIZE * 1f;
			playerGO.transform.position = new Vector3(0f, 0f, stride + 2f);

			// Frame 1: orchestrate batch
			yield return null;
			world
				.GetOrCreateSystem<Voxels.Core.Meshing.Systems.RollingGridOrchestratorSystem>()
				.Update(world.Unmanaged);
			world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>().Update();

			// Complete batch fence and commit
			VoxelJobFenceRegistry.Initialize();
			VoxelJobFenceRegistry.CompleteAndReset(gridEntity);
			for (var i = 0; i < 2; i++)
			{
				orchestrator.Update(world.Unmanaged);
				world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>().Update();
			}
			managed.Update();
			world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>().Update();
			trsSync.Update();

			// Assert grid anchor moved by one stride along Z (or X depending on sign logic)
			var pos = gridGO.transform.position;
			Assert.That(Mathf.Abs(pos.z), Is.EqualTo(stride).Within(0.25f));
			Assert.That(Mathf.Abs(pos.x), Is.EqualTo(0f).Within(0.25f));
			Assert.That(pos.y, Is.EqualTo(0f).Within(0.05f));

			Object.DestroyImmediate(playerGO);
			Object.DestroyImmediate(gridGO);
			world.Dispose();
			World.DefaultGameObjectInjectionWorld = prev;
		}

		static Entity FindGridEntityById(EntityManager em, int instanceId)
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
