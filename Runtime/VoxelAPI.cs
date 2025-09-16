namespace Voxels
{
	using Core.Concurrency;
	using Core.Hybrid;
	using Core.Meshing.Budgets;
	using Core.Meshing.Components;
	using Core.Spatial;
	using Core.Stamps;
	using Core.ThirdParty.SurfaceNets.Extensions;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using static Core.VoxelConstants;
	using static Unity.Mathematics.math;

	public static class VoxelAPI
	{
		public static class Budgets
		{
			public static void Apply(MeshingBudgets newBudgets)
			{
				if (!VoxelEntityBridge.TryGetEntityManager(out var em))
					return;

				var ent = em.CreateEntity(typeof(MeshingBudgetsChangeRequest));

				em.SetComponentData(ent, new MeshingBudgetsChangeRequest { newBudgets = newBudgets });
			}
		}

		public static class Query
		{
			public static byte GetMaterialAtPoint(float3 center, float extents = 1f)
			{
				if (!VoxelEntityBridge.TryGetEntityManager(out var em))
					return 0;
				var st = VoxelEntityBridge.GetSingleton<VoxelSpatialSystem.VoxelObjectHash>();
				var queryAABB = MinMaxAABB.CreateFromCenterAndExtents(center, extents);
				using var objects = st.Query(queryAABB);

				foreach (var spatialVoxelObject in objects)
				{
					VoxelJobFenceRegistry.CompleteAndReset(spatialVoxelObject.entity);

					var m = em.GetComponentData<NativeVoxelMesh>(spatialVoxelObject.entity);
					var materials = m.volume.materials;

					var queryLocalAABB = queryAABB.Transform(spatialVoxelObject.wtl);
					var queryLocalPoint = (int3)queryLocalAABB.Center;

					queryLocalPoint = clamp(queryLocalPoint, 0, CHUNK_SIZE_MINUS_ONE);

					var localIndex =
						(queryLocalPoint.x << X_SHIFT) + (queryLocalPoint.y << Y_SHIFT) + queryLocalPoint.z;

					return (byte)clamp(materials[localIndex] - 1, 0, 255); // -1 since 0=air
				}

				return 0;
			}
		}

		public static class Stamps
		{
			public static void ApplySphere(
				float3 center,
				float radius = 1f,
				float strength = -1f,
				float power = 1.5f,
				byte material = 0
			)
			{
				Apply(
					new NativeVoxelStampProcedural
					{
						shape = new ProceduralShape
						{
							shape = ProceduralShape.Shape.SPHERE,
							sphere = new ProceduralSphere { center = center, radius = radius },
						},
						bounds = MinMaxAABB.CreateFromCenterAndExtents(center, radius * 2f),
						strength = strength,
						shapePower = power,
						material = material,
					}
				);
			}

			public static void Apply(NativeVoxelStampProcedural stamp)
			{
				if (!VoxelEntityBridge.TryGetEntityManager(out var em))
					return;

				var ent = em.CreateEntity(typeof(NativeVoxelStampProcedural));

				em.SetComponentData(ent, stamp);
			}
		}
	}
}
