namespace Voxels.Runtime.Tests
{
	using System.Collections;
	using Core.Concurrency;
	using Core.Grids;
	using Core.Hybrid;
	using Core.Meshing;
	using Core.Meshing.Systems;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using Unity.Transforms;
	using UnityEngine;
	using UnityEngine.TestTools;

	public class RollingGridMultiRepositionTests
	{
		World previous;
		World world;
		EntityManager em;

		[SetUp]
		public void Setup()
		{
			previous = World.DefaultGameObjectInjectionWorld;
			world = new World("RGMultiWorld");
			World.DefaultGameObjectInjectionWorld = world;
			em = world.EntityManager;
			world.CreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
			world.CreateSystem<VoxelJobFenceRegistrySystem>();
			world.CreateSystem<RollingGridOrchestratorSystem>();
			world.CreateSystemManaged<ManagedVoxelMeshingSystem>();
			world.CreateSystemManaged<EntityGameObjectTransformSystem>();
			Core.Meshing.SharedStaticMeshingResources.Initialize();
		}

		[TearDown]
		public void Teardown()
		{
			world.Dispose();
			World.DefaultGameObjectInjectionWorld = previous;
		}

		[UnityTest]
		public IEnumerator Grid_Repositions_Multiple_Steps_And_Commits()
		{
			// Anchor GO for grid
			var anchorGO = new GameObject("GridAnchor");
			anchorGO.transform.position = Vector3.zero;

			// Create grid entity with transform attachment
			var grid = em.CreateEntity(
				typeof(LocalToWorld),
				typeof(NativeVoxelGrid),
				typeof(RollingGridConfig)
			);
			var gridId = 12345;
			em.SetComponentData(
				grid,
				new NativeVoxelGrid
				{
					gridID = gridId,
					voxelSize = 1f,
					bounds = MinMaxAABB.CreateFromCenterAndExtents(float3.zero, new float3(120f, 30f, 120f)),
				}
			);
			em.SetComponentData(grid, new LocalToWorld { Value = float4x4.identity });
			em.SetComponentData(
				grid,
				new RollingGridConfig { enabled = true, slotDims = new int3(2, 1, 2) }
			);
			em.AddComponentData(
				grid,
				new EntityGameObjectTransformAttachment { attachTo = anchorGO.transform }
			);

			// LinkedEntityGroup and chunks with attachments
			var leg = em.AddBuffer<LinkedEntityGroup>(grid);
			leg.Add(grid);
			var stride = Voxels.Core.VoxelConstants.EFFECTIVE_CHUNK_SIZE * 1f;
			var chunkGOs = new System.Collections.Generic.List<Transform>();
			for (var x = 0; x < 2; x++)
			for (var z = 0; z < 2; z++)
			{
				var chunk = em.CreateEntity(
					typeof(NativeVoxelChunk),
					typeof(NativeVoxelMesh),
					typeof(Core.Meshing.Tags.NeedsManagedMeshUpdate),
					typeof(VoxelMeshingAlgorithmComponent)
				);
				em.SetComponentEnabled<Core.Meshing.Tags.NeedsManagedMeshUpdate>(chunk, true);
				em.SetComponentData(
					chunk,
					new NativeVoxelChunk { coord = new int3(x, 0, z), gridID = gridId }
				);
				var nvm = new NativeVoxelMesh(Allocator.Persistent);
				nvm.meshing.meshData = UnityEngine.Mesh.AllocateWritableMeshData(1);
				em.SetComponentData(chunk, nvm);
				em.SetComponentData(
					chunk,
					new VoxelMeshingAlgorithmComponent
					{
						algorithm = VoxelMeshingAlgorithm.NAIVE_SURFACE_NETS,
						normalsMode = NormalsMode.GRADIENT,
						materialDistributionMode = MaterialDistributionMode.BLENDED_CORNER_SUM,
					}
				);

				var chunkGO = new GameObject($"Chunk_{x}_{z}");
				chunkGOs.Add(chunkGO.transform);
				em.AddComponentData(
					chunk,
					new EntityGameObjectTransformAttachment { attachTo = chunkGO.transform }
				);
				// Re-fetch buffer after potential structural changes to avoid safety invalidation
				leg = em.GetBuffer<LinkedEntityGroup>(grid);
				leg.Add(new LinkedEntityGroup { Value = chunk });
			}

			// Helper locals
			var ecb = world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
			var orchestrator = world.GetOrCreateSystem<RollingGridOrchestratorSystem>();
			var managed = world.GetOrCreateSystemManaged<ManagedVoxelMeshingSystem>();
			var trsSync = world.GetOrCreateSystemManaged<EntityGameObjectTransformSystem>();

			// Step 1: +X
			em.AddComponent<RollingGridMoveRequest>(grid);
			em.SetComponentEnabled<RollingGridMoveRequest>(grid, true);
			em.SetComponentData(
				grid,
				new RollingGridMoveRequest { targetAnchorWorldChunk = new int3(1, 0, 0) }
			);
			orchestrator.Update(world.Unmanaged);
			ecb.Update();
			Core.Concurrency.VoxelJobFenceRegistry.CompleteAndReset(grid);
			for (var i = 0; i < 3; i++)
			{
				orchestrator.Update(world.Unmanaged);
				ecb.Update();
			}
			managed.Update();
			ecb.Update();
			trsSync.Update();

			var pos1 = anchorGO.transform.position;
			Assert.AreEqual(
				stride,
				Mathf.Max(Mathf.Abs(pos1.x), Mathf.Abs(pos1.z)),
				0.001f,
				"Anchor did not move by one stride"
			);
			Assert.AreEqual(
				0f,
				Mathf.Min(Mathf.Abs(pos1.x), Mathf.Abs(pos1.z)),
				0.001f,
				"Anchor moved along multiple axes"
			);
			Assert.AreEqual(0f, pos1.y, 0.001f);
			// Chunk GOs should be at slot*stride local positions
			Assert.AreEqual(new Vector3(0f, 0f, 0f), chunkGOs[0].localPosition);
			Assert.AreEqual(new Vector3(0f, 0f, stride), chunkGOs[1].localPosition);
			Assert.AreEqual(new Vector3(stride, 0f, 0f), chunkGOs[2].localPosition);
			Assert.AreEqual(new Vector3(stride, 0f, stride), chunkGOs[3].localPosition);

			// Step 2: +Z
			em.SetComponentEnabled<RollingGridMoveRequest>(grid, true);
			em.SetComponentData(
				grid,
				new RollingGridMoveRequest { targetAnchorWorldChunk = new int3(1, 0, 1) }
			);
			orchestrator.Update(world.Unmanaged);
			ecb.Update();
			Core.Concurrency.VoxelJobFenceRegistry.CompleteAndReset(grid);
			for (var i = 0; i < 3; i++)
			{
				orchestrator.Update(world.Unmanaged);
				ecb.Update();
			}
			managed.Update();
			ecb.Update();
			trsSync.Update();

			var pos2 = anchorGO.transform.position;
			Assert.AreEqual(stride, Mathf.Abs(pos2.x), 0.001f);
			Assert.AreEqual(stride, Mathf.Abs(pos2.z), 0.001f);
			Assert.AreEqual(0f, pos2.y, 0.001f);

			// Final assertions: flags cleared and mesh applied on chunks
			Assert.IsFalse(
				em.IsComponentEnabled<RollingGridCommitEvent>(grid),
				"Commit event should be disabled post-commit"
			);
			if (em.HasComponent<RollingGridBatchActive>(grid))
				Assert.IsFalse(
					em.IsComponentEnabled<RollingGridBatchActive>(grid),
					"BatchActive should be disabled post-commit"
				);
			using (var arr = em.CreateEntityQuery(typeof(NativeVoxelChunk)).ToEntityArray(Allocator.Temp))
			{
				for (var i = 0; i < arr.Length; i++)
				{
					var e = arr[i];
					if (em.HasComponent<Core.Meshing.Tags.NeedsManagedMeshUpdate>(e))
						Assert.IsFalse(
							em.IsComponentEnabled<Core.Meshing.Tags.NeedsManagedMeshUpdate>(e),
							"NeedsManagedMeshUpdate should be disabled after commit"
						);
					var nvm = em.GetComponentData<NativeVoxelMesh>(e);
					Assert.Zero(nvm.meshing.meshData.Length);
				}
			}

			// Cleanup
			foreach (var t in chunkGOs)
				Object.DestroyImmediate(t.gameObject);
			Object.DestroyImmediate(anchorGO);
			yield return null;
		}
	}
}
