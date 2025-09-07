namespace Voxels.Core.Procedural.Scheduling
{
	using Atlasing.Components;
	using Budgets;
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

			var toProcess = VoxelBudgets.Current.perFrame.proceduralScheduled;

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
					var job = pcg.generator.Schedule(
						voxelObject.localBounds,
						ltw.Value,
						voxelObject.voxelSize,
						mesh.volume,
						GetFence(entity)
					);

					UpdateFence(entity, job);
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
