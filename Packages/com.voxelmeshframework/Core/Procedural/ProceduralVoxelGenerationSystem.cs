namespace Voxels.Core.Procedural
{
	using Concurrency;
	using Grids;
	using Meshing;
	using Meshing.Tags;
	using Tags;
	using Unity.Entities;
	using Unity.Jobs;
	using Unity.Transforms;
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

				// Avoid scheduling writes while previous work for this entity is still in-flight
#if !VMF_TAIL_PIPELINE
				if (!VoxelJobFenceRegistry.TryComplete(entity))
					continue;
#endif

				using (ProceduralVoxelGenerationSystem_Schedule.Auto())
				{
					var job = pcg.generator.Schedule(
						voxelObject.localBounds,
						ltw.Value,
						voxelObject.voxelSize,
						mesh.volume,
						VoxelJobFenceRegistry.Get(entity)
					);

					VoxelJobFenceRegistry.Update(entity, job);
				}

				ecb.SetComponentEnabled<NeedsProceduralUpdate>(entity, false);
				ecb.SetComponentEnabled<NeedsRemesh>(entity, true);
			}

			JobHandle.ScheduleBatchedJobs();
		}
	}
}
