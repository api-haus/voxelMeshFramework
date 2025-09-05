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
	using Unity.Mathematics.Geometry;
	using UnityEngine;
	using UnityEngine.TestTools;

	public class RollingGridPlayerDriverCommitTests
	{
		[UnityTest]
		public IEnumerator Driver_Move_Produces_Grid_Anchor_Move()
		{
			// Isolate in a fresh World so no prior chunks/jobs interfere
			var prev = World.DefaultGameObjectInjectionWorld;
			var world = new World("RGDriverCommitWorld");
			World.DefaultGameObjectInjectionWorld = world;
			var em = world.EntityManager;
			world.CreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
			world.CreateSystem<Voxels.Core.Concurrency.VoxelJobFenceRegistrySystem>();
			var orchestrator =
				world.CreateSystem<Voxels.Core.Meshing.Systems.RollingGridOrchestratorSystem>();
			var managed =
				world.CreateSystemManaged<Voxels.Core.Meshing.Systems.ManagedVoxelMeshingSystem>();
			var trsSync = world.CreateSystemManaged<Voxels.Core.Hybrid.EntityGameObjectTransformSystem>();

			// Create grid GameObject with authoring component
			var gridGO = new GameObject("GridHost");
			gridGO.SetActive(false);
			var grid = gridGO.AddComponent<VoxelMeshGrid>();
			// Configure serialized fields before Awake
			grid.__SetRolling(true);
			grid.__SetExternalDriver(true);
			grid.__SetVoxelSize(1f);
			grid.__SetWorldBounds(new Bounds(Vector3.zero, new Vector3(120f, 30f, 120f)));
			// Provide a procedural generator to satisfy chunk allocation settings copy
			var gen = gridGO.AddComponent<HalfSphereVoxelGenerator>();
			grid.__SetProcedural(gen);
			gridGO.SetActive(true);

			// Create player with driver
			var playerGO = new GameObject("Player");
			playerGO.SetActive(false);
			var driver = playerGO.AddComponent<RollingGridPlayerDriver>();
			driver.__SetGrid(grid);
			playerGO.SetActive(true);

			// Immediately disable chunk allocation on the grid root to avoid creating chunk entities
			var gridEntity = FindGridEntityById(em, gridGO.GetInstanceID());
			Assert.AreNotEqual(Entity.Null, gridEntity, "Grid entity not found");
			if (em.HasComponent<NeedsChunkAllocation>(gridEntity))
				em.SetComponentEnabled<NeedsChunkAllocation>(gridEntity, false);
			if (em.HasComponent<Core.Procedural.PopulateWithProceduralVoxelGenerator>(gridEntity))
				em.RemoveComponent<Core.Procedural.PopulateWithProceduralVoxelGenerator>(gridEntity);

			// Allow Awake/initialization to settle
			yield return null;

			// Move the player across +X boundary (> stride)
			var stride = Voxels.Core.VoxelConstants.EFFECTIVE_CHUNK_SIZE * 1f;
			playerGO.transform.position = new Vector3(stride + 2f, 0f, 0f);

			// Let driver send, then run orchestrator -> ecb, force-complete batch, and commit
			for (var i = 0; i < 1; i++)
				yield return null;
			world
				.GetOrCreateSystem<Voxels.Core.Meshing.Systems.RollingGridOrchestratorSystem>()
				.Update(world.Unmanaged);
			world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>().Update();
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

			// Assert grid anchor GameObject moved by one stride along a single axis
			var pos = gridGO.transform.position;
			Assert.AreEqual(
				stride,
				Mathf.Max(Mathf.Abs(pos.x), Mathf.Abs(pos.z)),
				0.25f,
				"Grid did not move by one stride"
			);
			Assert.AreEqual(
				0f,
				Mathf.Min(Mathf.Abs(pos.x), Mathf.Abs(pos.z)),
				0.25f,
				"Grid moved along multiple axes"
			);
			Assert.AreEqual(0f, pos.y, 0.01f);

			// Optional: verify commit flags on grid entity toggled
			var world2 = World.DefaultGameObjectInjectionWorld;
			em = world2.EntityManager;
			gridEntity = FindGridEntityById(em, gridGO.GetInstanceID());
			Assert.AreNotEqual(Entity.Null, gridEntity);
			if (em.HasComponent<RollingGridCommitEvent>(gridEntity))
				Assert.IsFalse(
					em.IsComponentEnabled<RollingGridCommitEvent>(gridEntity),
					"Commit event should be disabled after processing"
				);
			if (em.HasComponent<RollingGridBatchActive>(gridEntity))
				Assert.IsFalse(
					em.IsComponentEnabled<RollingGridBatchActive>(gridEntity),
					"BatchActive should be disabled after processing"
				);

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
