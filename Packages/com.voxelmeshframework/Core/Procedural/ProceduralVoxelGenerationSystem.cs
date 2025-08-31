namespace Voxels.Core.Procedural
{
	using Grids;
	using Meshing;
	using Meshing.Tags;
	using Tags;
	using Unity.Entities;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Transforms;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Entities.SystemAPI;
	using static Unity.Mathematics.Geometry.Math;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;

	[RequireMatchingQueriesForUpdate]
	public partial class ProceduralVoxelGenerationSystem : SystemBase
	{
		protected override void OnUpdate()
		{
			using var _ = ProceduralVoxelGenerationSystem_Update.Auto();

			var beginProfileJob = Dependency;
			var concurrentJobs = beginProfileJob;

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

				using (ProceduralVoxelGenerationSystem_Schedule.Auto())
				{
					var job = pcg.generator.Schedule(
						Transform((float3x3)ltw.Value, voxelObject.localBounds),
						voxelObject.voxelSize,
						mesh.volume,
						beginProfileJob
					);

					concurrentJobs = JobHandle.CombineDependencies(job, concurrentJobs);
				}

				ecb.SetComponentEnabled<NeedsProceduralUpdate>(entity, false);
				ecb.SetComponentEnabled<NeedsRemesh>(entity, true);
			}

			Dependency = concurrentJobs;
		}
	}
}
