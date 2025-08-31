namespace Voxels.Tests.Editor
{
	using Core.Authoring;
	using Core.Grids;
	using Core.Meshing.Systems;
	using Core.Spatial;
	using Core.Stamps;
	using NUnit.Framework;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using UnityEngine;
	using static Core.VoxelConstants;

	[TestFixture]
	public class StampingEcsEndToEndTests
	{
		[Test]
		public void EndToEnd_Stamping_ProducesVertices()
		{
			// Ensure a Default World exists
			var world = World.DefaultGameObjectInjectionWorld;
			if (world == null)
			{
				Unity.Entities.DefaultWorldInitialization.Initialize("Test World", false);
				world = World.DefaultGameObjectInjectionWorld;
			}

			var initGroup = world.GetOrCreateSystemManaged<InitializationSystemGroup>();
			var simGroup = world.GetOrCreateSystemManaged<SimulationSystemGroup>();

			// Create a VoxelMesh authoring object and bridge it to ECS
			var go = new GameObject("VM_EndToEnd");
			Entity ent = Entity.Null;
			try
			{
				go.AddComponent<MeshFilter>();
				go.AddComponent<MeshRenderer>();
				var vm = go.AddComponent<VoxelMesh>();
				vm.voxelSize = 1f;

				// Explicitly create the ECS entity (avoid relying on Awake in edit mode)
				ent = Core.VoxelEntityBridge.CreateVoxelMeshEntity(vm, go.GetInstanceID(), go.transform);

				// Set local bounds for spatial hashing (32^3 volume starting at origin)
				var em = world.EntityManager;
				var obj = em.GetComponentData<NativeVoxelObject>(ent);
				obj.localBounds = new MinMaxAABB(float3.zero, new float3(CHUNK_SIZE));
				em.SetComponentData(ent, obj);

				// Run initialization to allocate NativeVoxelMesh and build spatial hash
				initGroup.Update();
				initGroup.Update();

				// Place a stamp into the center of the volume via public API
				var center = new float3(16, 16, 16);
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

				// Pump simulation/initialization a few steps to process stamping and meshing
				for (var i = 0; i < 6; i++)
				{
					simGroup.Update();
					initGroup.Update();
				}

				// Verify meshing produced some vertices
				var nvm = em.GetComponentData<Core.Meshing.NativeVoxelMesh>(ent);
				Assert.Greater(
					nvm.meshing.vertices.Length,
					0,
					"Meshing should produce vertices after stamping"
				);
			}
			finally
			{
				if (ent != Entity.Null)
					Core.VoxelEntityBridge.DestroyEntity(ent);
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void EndToEnd_Stamping_WithRotatedTransform_ProducesVertices()
		{
			// Ensure a Default World exists
			var world = World.DefaultGameObjectInjectionWorld;
			if (world == null)
			{
				Unity.Entities.DefaultWorldInitialization.Initialize("Test World", false);
				world = World.DefaultGameObjectInjectionWorld;
			}

			var initGroup = world.GetOrCreateSystemManaged<InitializationSystemGroup>();
			var simGroup = world.GetOrCreateSystemManaged<SimulationSystemGroup>();

			// Create a VoxelMesh authoring object and apply a significant transform
			var go = new GameObject("VM_EndToEnd_Rotated");
			Entity ent = Entity.Null;
			try
			{
				go.AddComponent<MeshFilter>();
				go.AddComponent<MeshRenderer>();
				go.transform.position = new Vector3(50f, 10f, -20f);
				go.transform.rotation = Quaternion.Euler(35f, 60f, 15f);

				var vm = go.AddComponent<VoxelMesh>();
				vm.voxelSize = 1f;

				// Explicitly create the ECS entity
				ent = Core.VoxelEntityBridge.CreateVoxelMeshEntity(vm, go.GetInstanceID(), go.transform);

				// Set local bounds for spatial hashing (32^3 volume starting at origin)
				var em = world.EntityManager;
				var obj = em.GetComponentData<NativeVoxelObject>(ent);
				obj.localBounds = new MinMaxAABB(float3.zero, new float3(CHUNK_SIZE));
				em.SetComponentData(ent, obj);

				// Run initialization to allocate NativeVoxelMesh and build spatial hash
				initGroup.Update();
				initGroup.Update();

				// Place a stamp at the world-space position corresponding to the local center
				var localCenter = new Vector3(16f, 16f, 16f);
				var worldCenter = go.transform.TransformPoint(localCenter);
				var radius = 4f;
				var stamp = new NativeVoxelStampProcedural
				{
					shape = new ProceduralShape
					{
						shape = ProceduralShape.Shape.SPHERE,
						sphere = new ProceduralSphere { center = (float3)worldCenter, radius = radius },
					},
					bounds = MinMaxAABB.CreateFromCenterAndExtents((float3)worldCenter, radius * 2f),
					strength = 1f,
					material = 2,
				};

				VoxelAPI.Stamp(stamp);

				// Pump groups to process stamping and meshing
				for (var i = 0; i < 8; i++)
				{
					simGroup.Update();
					initGroup.Update();
				}

				// Verify meshing produced some vertices
				var nvm = em.GetComponentData<Core.Meshing.NativeVoxelMesh>(ent);
				Assert.Greater(
					nvm.meshing.vertices.Length,
					0,
					"Meshing should produce vertices after stamping with rotation"
				);
			}
			finally
			{
				if (ent != Entity.Null)
					Core.VoxelEntityBridge.DestroyEntity(ent);
				Object.DestroyImmediate(go);
			}
		}
	}
}
