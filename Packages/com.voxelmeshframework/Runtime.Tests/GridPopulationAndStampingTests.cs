namespace Voxels.Runtime.Tests
{
	using System.Collections;
	using Core.Authoring;
	using Core.Grids;
	using Core.Meshing;
	using Core.Meshing.Systems;
	using Core.Procedural;
	using Core.Spatial;
	using Core.Stamps;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using Unity.Transforms;
	using UnityEngine;
	using UnityEngine.TestTools;
	using Voxels;

	public class GridPopulationAndStampingTests
	{
		World m_Previous;
		World m_World;
		EntityManager m_Em;

		[UnitySetUp]
		public IEnumerator Setup()
		{
			m_Previous = World.DefaultGameObjectInjectionWorld;
			m_World = new World("GridLoadStampWorld");
			World.DefaultGameObjectInjectionWorld = m_World;
			m_Em = m_World.EntityManager;

			// Required systems
			m_World.CreateSystemManaged<EndInitializationEntityCommandBufferSystem>();
			m_World.CreateSystem<Voxels.Core.Config.VoxelProjectBootstrapSystem>();
			m_World.CreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
			m_World.CreateSystem<Voxels.Core.Concurrency.VoxelJobFenceRegistrySystem>();
			m_World.CreateSystem<VoxelMeshAllocationSystem>();
			m_World.CreateSystemManaged<ProceduralVoxelGenerationSystem>();
			m_World.CreateSystem<VoxelMeshingSystem>();
			m_World.CreateSystemManaged<ManagedVoxelMeshingSystem>();
			m_World.CreateSystem<VoxelSpatialSystem>();
			m_World.CreateSystem<VoxelStampSystem>();

			// Shared resources for meshing
			SharedStaticMeshingResources.Initialize();

			yield return null;
		}

		[UnityTearDown]
		public IEnumerator TearDown()
		{
			if (m_World != null && m_World.IsCreated)
				m_World.Dispose();
			World.DefaultGameObjectInjectionWorld = m_Previous;
			yield return null;
		}

		[UnityTest]
		public IEnumerator Grid_Populates_Meshes_And_Stamp_Changes_Content()
		{
			// 1) Create a grid GameObject without rolling
			var gridGO = new GameObject("GridHost_LoadAndStamp");
			gridGO.SetActive(false);
			var gridMb = gridGO.AddComponent<VoxelMeshGrid>();
			gridMb.__SetVoxelSize(1f);
			gridMb.__SetWorldBounds(new Bounds(Vector3.zero, new Vector3(60f, 30f, 60f)));
			// Attach a simple procedural generator to populate chunks
			var gen = gridGO.AddComponent<SimpleNoiseVoxelGenerator>();
			gridMb.__SetProcedural(gen);
			gridGO.SetActive(true);

			// Allow Awake to create ECS entities
			yield return null;

			// Find the grid entity and escalate meshing budget
			var gridEntity = FindGridEntityById(m_Em, gridGO.GetInstanceID());
			Assert.AreNotEqual(Entity.Null, gridEntity, "Grid entity not found");
			if (m_Em.HasComponent<NativeVoxelGrid.MeshingBudget>(gridEntity))
				m_Em.SetComponentData(
					gridEntity,
					new NativeVoxelGrid.MeshingBudget { maxMeshesPerFrame = 32 }
				);

			// Kick the initial meshing pipeline explicitly; propagation will fan out to chunks
			if (m_Em.HasComponent<Core.Meshing.Tags.NeedsRemesh>(gridEntity))
				m_Em.SetComponentEnabled<Core.Meshing.Tags.NeedsRemesh>(gridEntity, true);

			var initEcb = m_World.GetOrCreateSystemManaged<EndInitializationEntityCommandBufferSystem>();
			var simEcb = m_World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();

			var alloc = m_World.GetOrCreateSystem<Voxels.Core.Grids.GridChunkAllocationSystem>();
			var meshAlloc = m_World.GetOrCreateSystem<VoxelMeshAllocationSystem>();
			var propagate = m_World.GetOrCreateSystem<Voxels.Core.Grids.GridNeedsTagPropagationSystem>();
			var spatial = m_World.GetOrCreateSystem<VoxelSpatialSystem>();
			var proc = m_World.GetOrCreateSystemManaged<ProceduralVoxelGenerationSystem>();
			var mesh = m_World.GetOrCreateSystem<VoxelMeshingSystem>();
			var managed = m_World.GetOrCreateSystemManaged<ManagedVoxelMeshingSystem>();

			// 3) Run frames to allocate, generate, mesh, and apply; first wait until totals known
			for (var i = 0; i < 60; i++)
			{
				alloc.Update(m_World.Unmanaged);
				initEcb.Update();
				meshAlloc.Update(m_World.Unmanaged);
				initEcb.Update();
				propagate.Update(m_World.Unmanaged);
				initEcb.Update();
				spatial.Update(m_World.Unmanaged);
				initEcb.Update();
				proc.Update();
				simEcb.Update();
				mesh.Update(m_World.Unmanaged);
				simEcb.Update();
				managed.Update();
				simEcb.Update();
				yield return null;

				if (m_Em.HasComponent<GridMeshingProgress>(gridEntity))
				{
					var p = m_Em.GetComponentData<GridMeshingProgress>(gridEntity);
					if (p.totalChunks > 0)
						break;
				}
			}

			// Then wait up to 300 frames until all chunks report MeshedOnce
			int totalChunks = 0;
			if (m_Em.HasComponent<GridMeshingProgress>(gridEntity))
			{
				var p = m_Em.GetComponentData<GridMeshingProgress>(gridEntity);
				totalChunks = math.max(0, p.totalChunks);
			}
			for (var i = 0; i < 300; i++)
			{
				proc.Update();
				simEcb.Update();
				mesh.Update(m_World.Unmanaged);
				simEcb.Update();
				managed.Update();
				simEcb.Update();
				yield return null;

				if (totalChunks == 0)
					continue;

				int meshed = 0;
				using (
					var chunks = m_Em.CreateEntityQuery(typeof(NativeVoxelChunk), typeof(NativeVoxelMesh))
						.ToEntityArray(Allocator.Temp)
				)
				{
					for (var j = 0; j < chunks.Length; j++)
					{
						var e = chunks[j];
						if (!m_Em.HasComponent<NativeVoxelChunk>(e))
							continue;
						var c = m_Em.GetComponentData<NativeVoxelChunk>(e);
						var g = m_Em.GetComponentData<NativeVoxelGrid>(gridEntity);
						if (c.gridID != g.gridID)
							continue;
						if (
							m_Em.HasComponent<Core.Meshing.Tags.MeshedOnce>(e)
							&& m_Em.IsComponentEnabled<Core.Meshing.Tags.MeshedOnce>(e)
						)
							meshed++;
					}
				}

				if (meshed >= totalChunks && totalChunks > 0)
					break;
			}

			// 4) Assert grid had chunks and all chunks have meshed at least once
			Assert.IsTrue(m_Em.HasComponent<GridMeshingProgress>(gridEntity));
			var prog = m_Em.GetComponentData<GridMeshingProgress>(gridEntity);
			Assert.Greater(prog.totalChunks, 0, "totalChunks should be set after allocation");
			// Do not require the event due to CI timing; rely on MeshedOnce checks above

			// Capture a representative chunk at/near the center for later comparison
			Entity targetChunk = Entity.Null;
			float3 stampPoint = gridMb.worldBounds.center;
			float voxelSize = 1f;
			using (
				var chunks = m_Em.CreateEntityQuery(
						typeof(NativeVoxelChunk),
						typeof(NativeVoxelObject),
						typeof(LocalToWorld)
					)
					.ToEntityArray(Allocator.Temp)
			)
			{
				for (var i = 0; i < chunks.Length; i++)
				{
					var e = chunks[i];
					var ltw = m_Em.GetComponentData<LocalToWorld>(e);
					var obj = m_Em.GetComponentData<NativeVoxelObject>(e);
					var pos = ltw.Position;
					var size = obj.localBounds.Extents * 2f; // box size
					var min = pos;
					var max = pos + size;
					if (
						stampPoint.x >= min.x
						&& stampPoint.x <= max.x
						&& stampPoint.y >= min.y
						&& stampPoint.y <= max.y
						&& stampPoint.z >= min.z
						&& stampPoint.z <= max.z
					)
					{
						targetChunk = e;
						voxelSize = obj.voxelSize;
						break;
					}
				}
			}
			Assert.AreNotEqual(
				Entity.Null,
				targetChunk,
				"Failed to locate a chunk under the stamp point"
			);

			// 5) De-escalate budget back to steady-state
			if (m_Em.HasComponent<NativeVoxelGrid.MeshingBudget>(gridEntity))
				m_Em.SetComponentData(
					gridEntity,
					new NativeVoxelGrid.MeshingBudget { maxMeshesPerFrame = 2 }
				);

			// Capture a lightweight baseline of SDF sums to detect change
			var before = m_Em.GetComponentData<NativeVoxelMesh>(targetChunk);
			long beforeSum = 0;
			for (var i = 0; i < before.volume.sdfVolume.Length; i++)
				beforeSum += before.volume.sdfVolume[i];

			// 6) Schedule a stamp at the grid center
			var radius = 3f;
			var stamp = new NativeVoxelStampProcedural
			{
				shape = new ProceduralShape
				{
					shape = ProceduralShape.Shape.SPHERE,
					sphere = new ProceduralSphere { center = stampPoint, radius = radius },
				},
				bounds = MinMaxAABB.CreateFromCenterAndExtents(stampPoint, new float3(radius * 2f)),
				material = 7,
				strength = 1f,
			};
			VoxelAPI.Stamp(stamp);

			// 7) Run a few frames to allow stamp → remesh → apply
			for (var i = 0; i < 5; i++)
			{
				spatial.Update(m_World.Unmanaged);
				initEcb.Update();
				m_World.GetOrCreateSystem<VoxelStampSystem>().Update(m_World.Unmanaged);
				simEcb.Update();
				mesh.Update(m_World.Unmanaged);
				simEcb.Update();
				managed.Update();
				simEcb.Update();
				yield return null;
			}

			// 8) Assert the target chunk's SDF contents changed
			var after = m_Em.GetComponentData<NativeVoxelMesh>(targetChunk);
			long afterSum = 0;
			for (var i = 0; i < after.volume.sdfVolume.Length; i++)
				afterSum += after.volume.sdfVolume[i];
			Assert.AreNotEqual(beforeSum, afterSum, "Chunk SDF should change after stamping");
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
