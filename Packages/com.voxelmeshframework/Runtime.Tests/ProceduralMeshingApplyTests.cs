namespace Voxels.Runtime.Tests
{
	using Core.Concurrency;
	using Core.Grids;
	using Core.Meshing;
	using Core.Meshing.Systems;
	using Core.Procedural;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using Unity.Transforms;

	[TestFixture]
	public class ProceduralMeshingApplyTests
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
			world.CreateSystemManaged<ProceduralVoxelGenerationSystem>();
		}

		[TearDown]
		public void Teardown()
		{
			world.Dispose();
		}

		World world;
		EntityManager em;

		[Test]
		public void Procedural_Meshing_ManagedApply_EndToEnd_Works()
		{
			SharedStaticMeshingResources.Initialize();

			var e = em.CreateEntity(
				typeof(LocalToWorld),
				typeof(NativeVoxelObject),
				typeof(NativeVoxelMesh),
				typeof(VoxelMeshingAlgorithmComponent),
				typeof(Core.Meshing.Tags.NeedsRemesh),
				typeof(Core.Meshing.Tags.NeedsManagedMeshUpdate),
				typeof(PopulateWithProceduralVoxelGenerator),
				typeof(Core.Procedural.Tags.NeedsProceduralUpdate)
			);
			em.SetComponentEnabled<Core.Meshing.Tags.NeedsRemesh>(e, false);
			em.SetComponentEnabled<Core.Meshing.Tags.NeedsManagedMeshUpdate>(e, false);
			em.SetComponentEnabled<Core.Procedural.Tags.NeedsProceduralUpdate>(e, true);

			em.SetComponentData(e, new LocalToWorld { Value = float4x4.identity });
			em.SetComponentData(
				e,
				new NativeVoxelObject
				{
					voxelSize = 0.5f,
					localBounds = MinMaxAABB.CreateFromCenterAndExtents(float3.zero, new float3(32f)),
				}
			);

			var nvm = new NativeVoxelMesh(Allocator.Persistent);
			em.SetComponentData(e, nvm);

			em.SetComponentData(
				e,
				new PopulateWithProceduralVoxelGenerator
				{
					generator = new HemisphereIslandGenerator
					{
						radius = 12f,
						planeY = 10f,
						sdfSamplesPerVoxel = 8,
					},
				}
			);

			em.SetComponentData(
				e,
				new VoxelMeshingAlgorithmComponent
				{
					algorithm = VoxelMeshingAlgorithm.NAIVE_SURFACE_NETS,
					normalsMode = NormalsMode.GRADIENT,
					materialDistributionMode = MaterialDistributionMode.BLENDED_CORNER_SUM,
				}
			);

			// Run multiple ticks to allow job fences to complete deterministically in CI
			for (var i = 0; i < 3; i++)
			{
				world.GetOrCreateSystemManaged<ProceduralVoxelGenerationSystem>().Update();
				world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>().Update();
			}
			Assert.IsTrue(
				em.IsComponentEnabled<Core.Meshing.Tags.NeedsRemesh>(e),
				"NeedsRemesh should be enabled after procedural generation"
			);

			for (var i = 0; i < 3; i++)
			{
				world.GetOrCreateSystem<VoxelMeshingSystem>().Update(world.Unmanaged);
				world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>().Update();
			}
			var afterMeshing = em.GetComponentData<NativeVoxelMesh>(e);
			Assert.Greater(afterMeshing.meshing.meshData.Length, 0, "meshData not allocated by meshing");
			Assert.IsTrue(
				em.IsComponentEnabled<Core.Meshing.Tags.NeedsManagedMeshUpdate>(e),
				"NeedsManagedMeshUpdate should be enabled after meshing"
			);

			for (var i = 0; i < 6; i++)
			{
				world.GetOrCreateSystemManaged<ManagedVoxelMeshingSystem>().Update();
				world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>().Update();
			}
			// Ensure any remaining fences are completed before reading back
			Core.Concurrency.VoxelJobFenceRegistry.CompleteAndReset(e);
			world.GetOrCreateSystemManaged<ManagedVoxelMeshingSystem>().Update();
			world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>().Update();
			var afterApply = em.GetComponentData<NativeVoxelMesh>(e);
			Assert.Zero(
				afterApply.meshing.meshData.Length,
				"meshData not applied/disposed by managed apply"
			);
			Assert.IsFalse(
				em.IsComponentEnabled<Core.Meshing.Tags.NeedsManagedMeshUpdate>(e),
				"NeedsManagedMeshUpdate should be disabled after managed apply"
			);

			afterApply.Dispose();
			em.SetComponentData(e, afterApply);
		}
	}
}
