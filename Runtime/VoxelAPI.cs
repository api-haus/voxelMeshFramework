namespace Voxels
{
	using Core.Hybrid;
	using Core.Meshing.Budgets;
	using Core.Stamps;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;

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

		public static class Stamps
		{
			public static void ApplySphere(
				float3 center,
				float radius = 1f,
				float strength = -1f,
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
