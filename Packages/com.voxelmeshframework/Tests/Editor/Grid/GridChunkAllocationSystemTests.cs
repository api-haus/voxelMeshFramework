namespace Voxels.Tests.Editor
{
	using System.Collections.Generic;
	using Core.Grids;
	using Core.Meshing;
	using Core.Meshing.Tags;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using Unity.Transforms;
	using static Core.VoxelConstants;

	public class GridChunkAllocationSystemTests
	{
		[OneTimeSetUp]
		public void OneTimeSetUp()
		{
			if (World.DefaultGameObjectInjectionWorld == null)
				DefaultWorldInitialization.Initialize("VMF Test World", true);
		}

		[SetUp]
		public void SetUp()
		{
			var world = World.DefaultGameObjectInjectionWorld;
			Assert.That(world, Is.Not.Null);
			// Do not destroy all entities; other systems create required singletons.
		}

		static void EnsureInitGroupWithAllocationSystem(World world)
		{
			var initGroup = world.GetOrCreateSystemManaged<InitializationSystemGroup>();
			var endInitEcb = world.GetOrCreateSystemManaged<EndInitializationEntityCommandBufferSystem>();
			initGroup.AddSystemToUpdateList(endInitEcb);
			// Ensure allocation system is present
			var gridAlloc = world.GetOrCreateSystem<GridChunkAllocationSystem>();
			initGroup.AddSystemToUpdateList(gridAlloc);
			// Prevent grid tag propagation from consuming grid-level Needs* in these allocation-only tests
			var propagation = world.GetOrCreateSystem<GridNeedsTagPropagationSystem>();
			initGroup.RemoveSystemFromUpdateList(propagation);
			initGroup.SortSystems();
		}

		static Entity CreateGrid(EntityManager em, MinMaxAABB bounds, float voxelSize, int gridId = 1)
		{
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
			var grid = em.CreateEntity(types);

			// Initialize grid tags
			em.SetComponentEnabled<NeedsManagedMeshUpdate>(grid, false);
			em.SetComponentEnabled<NeedsRemesh>(grid, false);
			em.SetComponentEnabled<NeedsSpatialUpdate>(grid, false);
			em.SetComponentEnabled<NeedsChunkAllocation>(grid, true);

			em.SetComponentData(
				grid,
				new NativeVoxelGrid
				{
					gridID = gridId,
					voxelSize = voxelSize,
					bounds = bounds,
				}
			);
			em.SetComponentData(grid, new LocalToWorld { Value = float4x4.identity });
			em.SetComponentData(
				grid,
				new LocalTransform
				{
					Position = float3.zero,
					Rotation = quaternion.identity,
					Scale = 1f,
				}
			);

			// Ensure LinkedEntityGroup exists and contains root at index 0
			var leg = em.AddBuffer<LinkedEntityGroup>(grid);
			leg.Add(grid);

			return grid;
		}

		static NativeArray<Entity> GetChildChunks(EntityManager em, Entity grid)
		{
			var query = em.CreateEntityQuery(typeof(NativeVoxelChunk), typeof(Parent));
			var chunks = query.ToEntityArray(Allocator.Temp);
			var result = new NativeList<Entity>(Allocator.Temp);
			for (var i = 0; i < chunks.Length; i++)
			{
				var e = chunks[i];
				if (em.GetComponentData<Parent>(e).Value == grid)
					result.Add(e);
			}

			chunks.Dispose();
			var arr = new NativeArray<Entity>(result.AsArray(), Allocator.Temp);
			result.Dispose();
			return arr;
		}

		[Test]
		public void Allocates_1x1x1_CreatesChunk_AndLinksToGrid()
		{
			var world = World.DefaultGameObjectInjectionWorld;
			EnsureInitGroupWithAllocationSystem(world);
			var initGroup = world.GetOrCreateSystemManaged<InitializationSystemGroup>();
			var em = world.EntityManager;

			var voxelSize = 1f;
			var extent = EFFECTIVE_CHUNK_SIZE * voxelSize;
			var bounds = new MinMaxAABB(float3.zero, new float3(extent));
			var grid = CreateGrid(em, bounds, voxelSize, 123);

			initGroup.Update();
			initGroup.Update();

			var leg = em.GetBuffer<LinkedEntityGroup>(grid);
			Assert.That(leg.Length, Is.EqualTo(2), "LEG should contain root + one chunk");

			using var chunks = GetChildChunks(em, grid);
			Assert.That(chunks.Length, Is.EqualTo(1));
			var chunk = chunks[0];
			Assert.That(em.HasComponent<LocalTransform>(chunk));
			Assert.That(em.HasComponent<LocalToWorld>(chunk));
			Assert.That(em.HasComponent<Parent>(chunk));
			Assert.That(em.HasComponent<NativeVoxelChunk>(chunk));
			Assert.That(em.HasComponent<NativeVoxelMesh.Request>(chunk));

			var nvc = em.GetComponentData<NativeVoxelChunk>(chunk);
			var nvo = em.GetComponentData<NativeVoxelObject>(chunk);
			Assert.That(nvc.gridID, Is.EqualTo(123));
			Assert.That(nvo.voxelSize, Is.EqualTo(voxelSize));
			Assert.That(nvc.coord, Is.EqualTo(new int3(0)));
			Assert.That(nvo.localBounds.Min, Is.EqualTo(float3.zero));
			Assert.That(nvo.localBounds.Max, Is.EqualTo(new float3(extent)));

			Assert.IsFalse(
				em.IsComponentEnabled<NeedsChunkAllocation>(grid),
				"Grid NeedsChunkAllocation should be disabled after allocation"
			);
		}

		[Test]
		public void MultipleChunks_And_Placement_AreCorrect()
		{
			var world = World.DefaultGameObjectInjectionWorld;
			EnsureInitGroupWithAllocationSystem(world);
			var initGroup = world.GetOrCreateSystemManaged<InitializationSystemGroup>();
			var em = world.EntityManager;

			var voxelSize = 1f;
			var stride = EFFECTIVE_CHUNK_SIZE * voxelSize;
			var dims = new int3(2, 1, 1);
			var bounds = new MinMaxAABB(float3.zero, new float3(dims) * stride);
			var grid = CreateGrid(em, bounds, voxelSize, 321);

			initGroup.Update();
			initGroup.Update();

			using var chunks = GetChildChunks(em, grid);
			Assert.That(chunks.Length, Is.EqualTo(dims.x * dims.y * dims.z));

			var seen = new HashSet<string>();
			for (var i = 0; i < chunks.Length; i++)
			{
				var e = chunks[i];
				var nvc = em.GetComponentData<NativeVoxelChunk>(e);
				var key = $"{nvc.coord.x},{nvc.coord.y},{nvc.coord.z}";
				Assert.IsTrue(seen.Add(key), "Chunk coords must be unique");
				var lt = em.GetComponentData<LocalTransform>(e);
				var expected = bounds.Min + ((float3)nvc.coord * stride);
				Assert.That(
					math.distance(lt.Position, expected) < 1e-4f,
					$"Position mismatch for coord {key}"
				);
			}
		}

		[Test]
		public void TagInheritance_OnCreation_MirrorsGridEnablement()
		{
			var world = World.DefaultGameObjectInjectionWorld;
			EnsureInitGroupWithAllocationSystem(world);
			var initGroup = world.GetOrCreateSystemManaged<InitializationSystemGroup>();
			var em = world.EntityManager;

			var voxelSize = 1f;
			var stride = EFFECTIVE_CHUNK_SIZE * voxelSize;
			var bounds = new MinMaxAABB(float3.zero, new float3(stride));
			var grid = CreateGrid(em, bounds, voxelSize, 777);

			// Enable grid tags prior to allocation; allocation should mirror these to new chunks
			em.SetComponentEnabled<NeedsRemesh>(grid, true);
			em.SetComponentEnabled<NeedsManagedMeshUpdate>(grid, true);
			em.SetComponentEnabled<NeedsSpatialUpdate>(grid, true);

			initGroup.Update();
			initGroup.Update();

			using var chunks = GetChildChunks(em, grid);
			Assert.That(chunks.Length, Is.EqualTo(1));
			var chunk = chunks[0];

			Assert.IsTrue(em.IsComponentEnabled<NeedsRemesh>(chunk));
			Assert.IsTrue(em.IsComponentEnabled<NeedsManagedMeshUpdate>(chunk));
			Assert.IsTrue(em.IsComponentEnabled<NeedsSpatialUpdate>(chunk));

			// Grid tags are not consumed by allocation; propagation system handles consumption
			Assert.IsTrue(em.IsComponentEnabled<NeedsRemesh>(grid));
			Assert.IsTrue(em.IsComponentEnabled<NeedsManagedMeshUpdate>(grid));
			Assert.IsTrue(em.IsComponentEnabled<NeedsSpatialUpdate>(grid));
		}

		[Test]
		public void ZeroSizedBounds_AllocatesNoChunks_AndDisablesGridRequest()
		{
			var world = World.DefaultGameObjectInjectionWorld;
			EnsureInitGroupWithAllocationSystem(world);
			var initGroup = world.GetOrCreateSystemManaged<InitializationSystemGroup>();
			var em = world.EntityManager;

			var voxelSize = 1f;
			var bounds = new MinMaxAABB(float3.zero, float3.zero);
			var grid = CreateGrid(em, bounds, voxelSize, 999);

			initGroup.Update();
			initGroup.Update();

			var leg = em.GetBuffer<LinkedEntityGroup>(grid);
			Assert.That(leg.Length, Is.EqualTo(1), "LEG should only contain root for zero-sized bounds");
			using var chunks = GetChildChunks(em, grid);
			Assert.That(chunks.Length, Is.EqualTo(0));
			Assert.IsFalse(em.IsComponentEnabled<NeedsChunkAllocation>(grid));
		}

		[Test]
		public void LargeBounds_AreClampedTo64_PerAxis()
		{
			var world = World.DefaultGameObjectInjectionWorld;
			EnsureInitGroupWithAllocationSystem(world);
			var initGroup = world.GetOrCreateSystemManaged<InitializationSystemGroup>();
			var em = world.EntityManager;

			var voxelSize = 1f;
			var stride = EFFECTIVE_CHUNK_SIZE * voxelSize;
			var oversized = new int3(100, 1, 1);
			var bounds = new MinMaxAABB(float3.zero, (float3)oversized * stride);
			var grid = CreateGrid(em, bounds, voxelSize, 42);

			initGroup.Update();
			initGroup.Update();

			using var chunks = GetChildChunks(em, grid);
			Assert.That(chunks.Length, Is.EqualTo(64));
		}
	}
}
