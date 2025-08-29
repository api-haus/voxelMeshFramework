namespace Voxels.Core.Procedural
{
	using Grids;
	using Meshing;
	using Meshing.Tags;
	using Tags;
	using Unity.Entities;
	using Unity.Jobs;
	using Unity.Transforms;
	using static Unity.Entities.SystemAPI;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;

	[RequireMatchingQueriesForUpdate]
	public partial class ProceduralVoxelGenerationSystem : SystemBase
	{
		protected override void OnUpdate()
		{
			var concurrentJobs = Dependency;
			var ecb = SystemAPI.GetSingleton<EndSimST>().CreateCommandBuffer(World.Unmanaged);

			// foreach (
			// 	var (pcg, voxelMeshRef, voxelChunkRef, entity) in
			// 	//
			// 	Query<
			// 		// ReSharper disable once Unity.Entities.MustBeSurroundedWithRefRwRo
			// 		PopulateWithProceduralVoxelGenerator,
			// 		RefRO<NativeVoxelMesh>,
			// 		RefRO<NativeVoxelChunk>
			// 	>() //
			// 	.WithEntityAccess() //
			// 	.WithAll<NeedsProceduralUpdate>()
			// )
			// {
			// 	ref readonly var mesh = ref voxelMeshRef.ValueRO;
			// 	ref readonly var chunk = ref voxelChunkRef.ValueRO;
			//
			// 	var job = pcg.generator.Schedule(chunk.bounds, chunk.voxelSize, mesh.volume, Dependency);
			//
			// 	concurrentJobs = JobHandle.CombineDependencies(job, concurrentJobs);
			//
			// 	ecb.SetComponentEnabled<NeedsProceduralUpdate>(entity, false);
			// 	ecb.SetComponentEnabled<NeedsRemesh>(entity, true);
			// }

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
				ref readonly var mesh = ref voxelMeshRef.ValueRO;
				ref readonly var chunk = ref voxelObjectRef.ValueRO;

				var job = pcg.generator.Schedule(
					chunk.Bounds(ltwRef.ValueRO.Position),
					chunk.voxelSize,
					mesh.volume,
					Dependency
				);

				concurrentJobs = JobHandle.CombineDependencies(job, concurrentJobs);

				ecb.SetComponentEnabled<NeedsProceduralUpdate>(entity, false);
				ecb.SetComponentEnabled<NeedsRemesh>(entity, true);
			}

			Dependency = concurrentJobs;
		}
	}
}
