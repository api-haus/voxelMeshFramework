namespace Voxels.Core.Meshing.Systems
{
	using Tags;
	using Unity.Burst;
	using Unity.Entities;
	using Unity.Jobs;
	using UnityEngine;
	using static Unity.Entities.SystemAPI;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;

	[RequireMatchingQueriesForUpdate]
	public partial struct VoxelMeshingSystem : ISystem
	{
		// EntityQuery m_NeedsRemeshQuery;

		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			state.RequireForUpdate<EndSimST>();

			// m_NeedsRemeshQuery = QueryBuilder().WithAll<NativeVoxelMesh, NeedsRemesh>().Build();
			// state.RequireForUpdate(m_NeedsRemeshQuery);
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			var ecb = GetSingleton<EndSimST>().CreateCommandBuffer(state.WorldUnmanaged);

			var concurrentMeshingJobs = state.Dependency;

			// enqueue
			foreach (
				var (nativeVoxelMeshRef, entity) in Query<RefRW<NativeVoxelMesh>>()
					.WithAll<NeedsRemesh>()
					.WithEntityAccess()
			)
			{
				ref var nvm = ref nativeVoxelMeshRef.ValueRW;

				if (nvm.meshing.meshData.Length == 0)
					nvm.meshing.meshData = Mesh.AllocateWritableMeshData(1);

				var meshingJob = nvm.ScheduleMeshing(state.Dependency);

				concurrentMeshingJobs = JobHandle.CombineDependencies(meshingJob, concurrentMeshingJobs);

				ecb.SetComponentEnabled<NeedsRemesh>(entity, false);
				ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(entity, true);
			}

			state.Dependency = concurrentMeshingJobs;
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state) { }
	}
}
