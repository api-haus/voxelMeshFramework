namespace Voxels.Tests.Editor
{
	using System.Collections;
	using Core.Authoring;
	using Core.Concurrency;
	using Core.Grids;
	using Core.Meshing;
	using Core.Meshing.Systems;
	using Core.Meshing.Tags;
	using Core.Spatial;
	using Core.Stamps;
	using NUnit.Framework;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using Unity.Transforms;
	using UnityEngine;
	using UnityEngine.TestTools;
	using static Core.VoxelConstants;

	public class GridChunkAllocationAndMeshingTests
	{
		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			// Ensure an Entities world exists and player loop is wired
			if (World.DefaultGameObjectInjectionWorld == null)
			{
				DefaultWorldInitialization.Initialize("VMF Test World", true);
			}
			// Ensure shared/static resources and registry are initialized for tests
			if (!SharedStaticMeshingResources.EdgeTable.IsCreated)
				SharedStaticMeshingResources.Initialize();
			VoxelJobFenceRegistry.Initialize();
		}

		[UnityTest]
		public IEnumerator Grid_1x1x1_AllocatesChunk_And_StampsSphere_ProducesVertices()
		{
			// Fetch/ensure world + entity manager
			var world = World.DefaultGameObjectInjectionWorld;
			Assert.That(world, Is.Not.Null);
			var em = world.EntityManager;
			var initGroup = world.GetOrCreateSystemManaged<InitializationSystemGroup>();
			var simGroup = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
			var endInitEcb = world.GetOrCreateSystemManaged<EndInitializationEntityCommandBufferSystem>();
			initGroup.AddSystemToUpdateList(endInitEcb);
			var endSimEcb = world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
			simGroup.AddSystemToUpdateList(endSimEcb);
			// Ensure systems are present in groups
			var gridAllocHandle = world.GetOrCreateSystem<GridChunkAllocationSystem>();
			initGroup.AddSystemToUpdateList(gridAllocHandle);
			var voxelMeshAllocHandle = world.GetOrCreateSystem<VoxelMeshAllocationSystem>();
			initGroup.AddSystemToUpdateList(voxelMeshAllocHandle);
			var spatialHandle = world.GetOrCreateSystem<VoxelSpatialSystem>();
			initGroup.AddSystemToUpdateList(spatialHandle);
			initGroup.SortSystems();
			var stampHandle = world.GetOrCreateSystem<VoxelStampSystem>();
			simGroup.AddSystemToUpdateList(stampHandle);
			var meshingHandle = world.GetOrCreateSystem<VoxelMeshingSystem>();
			simGroup.AddSystemToUpdateList(meshingHandle);
			var managedMeshing = world.GetOrCreateSystemManaged<ManagedVoxelMeshingSystem>();
			simGroup.AddSystemToUpdateList(managedMeshing);
			simGroup.SortSystems();

			// Arrange: create a 1x1x1 grid entity directly
			var types = new ComponentType[]
			{
				typeof(LocalToWorld),
				typeof(LocalTransform),
				typeof(NativeVoxelGrid),
				typeof(NeedsRemesh),
				typeof(NeedsManagedMeshUpdate),
				typeof(NeedsSpatialUpdate),
				typeof(NeedsChunkAllocation),
			};
			var gridEntity = em.CreateEntity(types);
			// Initialize enableable tags on grid root
			em.SetComponentEnabled<NeedsManagedMeshUpdate>(gridEntity, false);
			em.SetComponentEnabled<NeedsRemesh>(gridEntity, false);
			em.SetComponentEnabled<NeedsSpatialUpdate>(gridEntity, false);
			em.SetComponentEnabled<NeedsChunkAllocation>(gridEntity, true);
			var voxelSize = 1f;
			var chunkExtent = EFFECTIVE_CHUNK_SIZE * voxelSize; // 30 units
			em.SetComponentData(
				gridEntity,
				new NativeVoxelGrid
				{
					gridID = 123,
					voxelSize = voxelSize,
					bounds = new MinMaxAABB(float3.zero, new float3(chunkExtent)),
				}
			);
			em.SetComponentData(gridEntity, new LocalToWorld { Value = float4x4.identity });
			em.SetComponentData(
				gridEntity,
				new LocalTransform
				{
					Position = float3.zero,
					Rotation = quaternion.identity,
					Scale = 1f,
				}
			);
			// Ensure LinkedEntityGroup exists and contains root
			var legInit = em.AddBuffer<LinkedEntityGroup>(gridEntity);
			legInit.Add(gridEntity);

			// Assert grid entity exists and has expected components
			var gridQuery = em.CreateEntityQuery(typeof(NativeVoxelGrid));
			Assert.That(gridQuery.CalculateEntityCount(), Is.GreaterThanOrEqualTo(1));
			Assert.That(em.HasComponent<LinkedEntityGroup>(gridEntity));
			Assert.That(em.HasComponent<NeedsChunkAllocation>(gridEntity));

			// Run initialization group to allocate chunks and process ECB
			initGroup.Update();
			initGroup.Update();

			// Verify at least one chunk was created and parented under the grid
			var chunkQuery = em.CreateEntityQuery(typeof(NativeVoxelChunk), typeof(Parent));
			var chunkEntities = chunkQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
			Entity chunkEntity = default;
			bool foundChild = false;
			for (int i = 0; i < chunkEntities.Length; i++)
			{
				var candidate = chunkEntities[i];
				var parent = em.GetComponentData<Parent>(candidate);
				if (parent.Value == gridEntity)
				{
					chunkEntity = candidate;
					foundChild = true;
					break;
				}
			}
			chunkEntities.Dispose();
			Assert.IsTrue(foundChild, "Expected at least one chunk parented under the grid");
			Assert.That(em.HasComponent<NativeVoxelChunk>(chunkEntity));
			Assert.That(em.HasComponent<LocalToWorld>(chunkEntity));
			Assert.That(em.HasComponent<LocalTransform>(chunkEntity));
			Assert.That(em.HasComponent<Parent>(chunkEntity));
			Assert.That(
				em.HasComponent<NativeVoxelMesh.Request>(chunkEntity),
				"Chunk should have allocation request"
			);

			// Run initialization again for VoxelMeshAllocationSystem to allocate NativeVoxelMesh
			initGroup.Update();
			Assert.That(
				em.HasComponent<NativeVoxelMesh>(chunkEntity),
				"Chunk should have allocated NativeVoxelMesh"
			);

			// Add default meshing algorithm if missing (grid chunks don’t add it by default)
			if (!em.HasComponent<VoxelMeshingAlgorithmComponent>(chunkEntity))
				em.AddComponentData(chunkEntity, VoxelMeshingAlgorithmComponent.Default);

			// Enable spatial update to register chunk in spatial hash
			if (!em.HasComponent<NeedsSpatialUpdate>(chunkEntity))
				em.AddComponent(chunkEntity, ComponentType.ReadWrite<NeedsSpatialUpdate>());
			em.SetComponentEnabled<NeedsSpatialUpdate>(chunkEntity, true);

			// Let VoxelSpatialSystem index the chunk
			initGroup.Update();

			// Act: stamp a small sphere at the grid center
			var center = float3.zero;
			var radius = 4f;
			var stamp = new NativeVoxelStampProcedural
			{
				shape = new ProceduralShape
				{
					shape = ProceduralShape.Shape.SPHERE,
					sphere = new ProceduralSphere { center = center, radius = radius },
				},
				bounds = MinMaxAABB.CreateFromCenterAndExtents(center, radius * 2f),
				strength = 1f,
				material = 1,
			};
			VoxelAPI.Stamp(stamp);

			// Run simulation group for stamping and meshing
			simGroup.Update(); // Stamp captured, writes scheduled
			simGroup.Update(); // Meshing enqueued
			simGroup.Update(); // Upload and managed handoff

			// Ensure background jobs for this chunk are complete before reading native buffers
			VoxelJobFenceRegistry.CompleteAndReset(chunkEntity);

			// Assert that meshing produced some vertices/indices
			var nvm = em.GetComponentData<NativeVoxelMesh>(chunkEntity);
			Assert.Greater(nvm.meshing.vertices.Length, 0, "Expected vertices after stamping");
			Assert.Greater(nvm.meshing.indices.Length, 0, "Expected indices after stamping");
			yield break;
		}
	}
}
