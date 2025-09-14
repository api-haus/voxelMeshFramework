namespace Voxels.Core.Procedural.Scheduling
{
	using Atlasing.Components;
	using Meshing.Budgets;
	using Meshing.Components;
	using Meshing.Tags;
	using Tags;
	using Unity.Entities;
	using Unity.Jobs;
	using Unity.Transforms;
	using static Concurrency.VoxelJobFenceRegistry;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Entities.SystemAPI;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;

	[RequireMatchingQueriesForUpdate]
	public partial class ProceduralVoxelGenerationSystem : SystemBase
	{
		protected override void OnUpdate()
		{
			using var _ = ProceduralVoxelGenerationSystem_Update.Auto();

			var ecb = SystemAPI.GetSingleton<EndSimST>().CreateCommandBuffer(World.Unmanaged);

			var toProcess = MeshingBudgets.Current.perFrame.proceduralSchedule;

			foreach (
				var (pcg, voxelObjectRef, voxelMeshRef, ltwRef, entity) in
				//
				Query<
					// ReSharper disable once Unity.Entities.MustBeSurroundedWithRefRwRo
					PopulateWithProceduralVoxelGenerator,
					RefRO<NativeVoxelObject>,
					RefRO<NativeVoxelMesh>,
					RefRO<LocalToWorld>
				>() //
				.WithEntityAccess() //
				.WithAll<NeedsProceduralUpdate>()
			)
			{
				ref readonly var ltw = ref ltwRef.ValueRO;
				ref readonly var mesh = ref voxelMeshRef.ValueRO;
				ref readonly var voxelObject = ref voxelObjectRef.ValueRO;

				using (ProceduralVoxelGenerationSystem_Schedule.Auto())
				{
					var jobHandle = GetFence(entity);

					jobHandle = pcg.voxels.ScheduleVoxels(
						voxelObject.localBounds,
						ltw.Value,
						voxelObject.voxelSize,
						mesh.volume,
						jobHandle
					);

					if (pcg.materials != null)
						jobHandle = pcg.materials.ScheduleMaterials(
							voxelObject.localBounds,
							ltw.Value,
							voxelObject.voxelSize,
							mesh.volume,
							jobHandle
						);

					UpdateFence(entity, jobHandle);
				}

				ecb.SetComponentEnabled<NeedsProceduralUpdate>(entity, false);
				ecb.SetComponentEnabled<NeedsRemesh>(entity, true);

				if (--toProcess <= 0)
					return;
			}

			JobHandle.ScheduleBatchedJobs();
		}
	}
}
