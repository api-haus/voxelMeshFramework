namespace Voxels.Core.Meshing.Systems
{
	using Concurrency;
	using Grids;
	using Procedural;
	using Stamps;
	using Tags;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Jobs;
	using Unity.Logging;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using Unity.Transforms;
	using UnityEngine;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Entities.SystemAPI;
	using static VoxelConstants;
	using EndSimST = Unity.Entities.EndSimulationEntityCommandBufferSystem.Singleton;

	/// <summary>
	///   Detects one-chunk rolling grid moves and schedules background work to populate and mesh the entering slab.
	///   Enforces single-axis, single-chunk steps and atomic commit via RollingGridCommitEvent.
	/// </summary>
	[RequireMatchingQueriesForUpdate]
	[UpdateInGroup(typeof(SimulationSystemGroup))]
	[UpdateBefore(typeof(VoxelMeshingSystem))]
	public partial struct RollingGridOrchestratorSystem : ISystem
	{
		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			state.RequireForUpdate<EndSimST>();
		}

		[BurstDiscard]
		public void OnUpdate(ref SystemState state)
		{
			using var _ = RollingGridOrchestratorSystem_Update.Auto();
			var ecb = GetSingleton<EndSimST>().CreateCommandBuffer(state.WorldUnmanaged);

			// Gather all grids configured for rolling that requested movement this frame
			var q = QueryBuilder().WithAll<NativeVoxelGrid, RollingGridConfig>().Build();
			using var grids = q.ToEntityArray(Allocator.Temp);
			using var gridConfigs = q.ToComponentDataArray<RollingGridConfig>(Allocator.Temp);
			if (grids.Length == 0)
				return;

			for (var gi = 0; gi < grids.Length; gi++)
			{
				var grid = grids[gi];
				var config = gridConfigs[gi];
				if (!config.enabled)
					continue;

				// Gate rolling moves until allocation complete and fully meshed event fired
				if (
					HasComponent<NeedsChunkAllocation>(grid) && IsComponentEnabled<NeedsChunkAllocation>(grid)
				)
				{
					Log.Debug("[RGOrch] Deferring move: grid allocation pending");
					if (HasComponent<RollingGridMoveRequest>(grid))
						ecb.SetComponentEnabled<RollingGridMoveRequest>(grid, false);
					continue;
				}
				// If meshing progress reports chunks exist, require FullyMeshedEvent; otherwise allow
				if (HasComponent<GridMeshingProgress>(grid))
				{
					var gp = GetComponent<GridMeshingProgress>(grid);
					if (gp.totalChunks > 0)
					{
						var hasEvt = HasComponent<NativeVoxelGrid.FullyMeshedEvent>(grid);
						var ready = hasEvt && IsComponentEnabled<NativeVoxelGrid.FullyMeshedEvent>(grid);
						if (!ready)
						{
							Log.Debug("[RGOrch] Deferring move: grid not fully meshed yet");
							if (HasComponent<RollingGridMoveRequest>(grid))
								ecb.SetComponentEnabled<RollingGridMoveRequest>(grid, false);
							continue;
						}
					}
				}

				var hasReq =
					HasComponent<RollingGridMoveRequest>(grid)
					&& IsComponentEnabled<RollingGridMoveRequest>(grid);
				if (!hasReq)
					continue;

				// If no chunks yet, allow move even if event component missing

				// Clamp while active
				if (
					HasComponent<RollingGridBatchActive>(grid)
					&& IsComponentEnabled<RollingGridBatchActive>(grid)
				)
				{
					ecb.SetComponentEnabled<RollingGridMoveRequest>(grid, false);
					continue;
				}

				var req = GetComponent<RollingGridMoveRequest>(grid);
				var gridData = GetComponent<NativeVoxelGrid>(grid);
				var dims = config.slotDims;

				// Compute current anchor from LocalToWorld and voxel size using floor; compare to requested
				var ltw = GetComponent<LocalToWorld>(grid);
				var origin = ltw.Position;
				var stride = gridData.voxelSize * EFFECTIVE_CHUNK_SIZE;
				int3 curAnchor = new(
					(int)math.floor(origin.x / stride),
					(int)math.floor(origin.y / stride),
					(int)math.floor(origin.z / stride)
				);

				var delta = req.targetAnchorWorldChunk - curAnchor;
				Log.Debug(
					"[RGOrch] Move req: gid={gid} curAnchor={cur} target={target} delta={delta}",
					gridData.gridID,
					curAnchor,
					req.targetAnchorWorldChunk,
					delta
				);

				// Ignore no-op
				if (math.all(delta == 0))
				{
					Log.Debug("[RGOrch] Ignored no-op move: delta={delta}", delta);
					ecb.SetComponentEnabled<RollingGridMoveRequest>(grid, false);
					continue;
				}

				// Mark batch active
				if (!HasComponent<RollingGridBatchActive>(grid))
					ecb.AddComponent<RollingGridBatchActive>(grid);
				ecb.SetComponentEnabled<RollingGridBatchActive>(grid, true);

				// Identify entering slab by the dominant axis of movement (supports multi-unit/multi-axis by taking one step toward target per batch)
				var absd = math.abs(delta);
				var axis = 0;
				var maxAbs = absd.x;
				if (absd.y > maxAbs)
				{
					axis = 1;
					maxAbs = absd.y;
				}
				if (absd.z > maxAbs)
				{
					axis = 2;
				}
				var positive = delta[axis] > 0;
				var enteringSlotCoord = positive ? dims[axis] - 1 : 0;
				var leavingSlotCoord = positive ? 0 : dims[axis] - 1;
				Log.Debug(
					"[RGOrch] Batch start: gid={gid} axis={axis} dir={dir} stride={stride}",
					gridData.gridID,
					axis,
					positive ? 1 : -1,
					stride
				);

				// Advance anchor by a single step along dominant axis
				int3 stepAnchor = curAnchor;
				stepAnchor[axis] += positive ? 1 : -1;

				// Collect all chunks for this grid
				var leg = GetBuffer<LinkedEntityGroup>(grid);
				var entering = new NativeList<Entity>(Allocator.Temp);
				var interior = new NativeList<Entity>(Allocator.Temp);

				for (var i = 1; i < leg.Length; i++)
				{
					var chunk = leg[i].Value;
					if (!HasComponent<NativeVoxelChunk>(chunk))
						continue;
					var c = GetComponent<NativeVoxelChunk>(chunk).coord;
					var slot = c; // initial allocation uses slot coords
					if (slot[axis] == enteringSlotCoord)
						entering.Add(chunk);
					else if (slot[axis] != leavingSlotCoord)
						interior.Add(chunk);
				}

				// Schedule generation -> apron copy -> mesh for entering slab
				JobHandle batchHandle = default;
				for (var i = 0; i < entering.Length; i++)
				{
					var chunk = entering[i];
					ref var nvm = ref GetComponentRW<NativeVoxelMesh>(chunk).ValueRW;

					// compute new world chunk coordinate for this slot given step anchor
					var slot = GetComponent<NativeVoxelChunk>(chunk).coord;
					var worldChunk = stepAnchor + slot;
					var originWorld = (float3)worldChunk * stride;
					var bounds = MinMaxAABB.CreateFromCenterAndExtents(
						originWorld + (new float3(CHUNK_SIZE * 0.5f) * gridData.voxelSize),
						new float3(CHUNK_SIZE * gridData.voxelSize)
					);

					// Generate
					var gen = default(JobHandle);
					if (state.EntityManager.HasComponent<PopulateWithProceduralVoxelGenerator>(chunk))
					{
						var pcg = state.EntityManager.GetComponentObject<PopulateWithProceduralVoxelGenerator>(
							chunk
						);
						gen = pcg.generator.Schedule(
							bounds,
							float4x4.identity,
							gridData.voxelSize,
							nvm.volume,
							VoxelJobFenceRegistry.GetFence(chunk)
						);
					}

					// Apron copies with interior neighbors
					for (var j = 0; j < interior.Length; j++)
					{
						var other = interior[j];
						var ca = slot;
						var cb = GetComponent<NativeVoxelChunk>(other).coord;
						if (!StampScheduling.TryResolveAdjacency(ca, cb, out var adjAxis, out var aIsSource))
							continue;
						var src = aIsSource ? chunk : other;
						var dst = aIsSource ? other : chunk;
						var dep = JobHandle.CombineDependencies(
							VoxelJobFenceRegistry.GetFence(src),
							VoxelJobFenceRegistry.GetFence(dst)
						);
						var srcNvm = GetComponentRW<NativeVoxelMesh>(src).ValueRO;
						var dstNvm = GetComponentRW<NativeVoxelMesh>(dst).ValueRO;
						var copy = StampScheduling.ScheduleCopySharedOverlap(srcNvm, dstNvm, adjAxis, dep);
						VoxelJobFenceRegistry.UpdateFence(src, copy);
						VoxelJobFenceRegistry.UpdateFence(dst, copy);
						gen = JobHandle.CombineDependencies(gen, copy);
					}

					// Mesh to staging
					var input = new MeshingInputData
					{
						volume = nvm.volume.sdfVolume,
						materials = nvm.volume.materials,
						voxelSize = gridData.voxelSize,
						edgeTable = SharedStaticMeshingResources.EdgeTable,
						chunkSize = CHUNK_SIZE,
						normalsMode = GetComponent<VoxelMeshingAlgorithmComponent>(chunk).normalsMode,
						materialDistributionMode = GetComponent<VoxelMeshingAlgorithmComponent>(
							chunk
						).materialDistributionMode,
						copyApronPostMesh = true,
					};
					var output = new MeshingOutputData
					{
						vertices = nvm.meshing.vertices,
						indices = nvm.meshing.indices,
						buffer = nvm.meshing.buffer,
						bounds = nvm.meshing.bounds,
					};
					var algo = GetComponent<VoxelMeshingAlgorithmComponent>(chunk);
					var meshingJob = MeshingScheduling.ScheduleAlgorithm(
						input,
						output,
						algo,
						ref nvm.meshing.fairing,
						gen
					);
					// allocate upload buffer
					nvm.meshing.meshData = Mesh.AllocateWritableMeshData(1);
					meshingJob = new UploadMeshJob
					{
						mda = nvm.meshing.meshData,
						bounds = nvm.meshing.bounds,
						indices = nvm.meshing.indices,
						vertices = nvm.meshing.vertices,
					}.Schedule(meshingJob);
					VoxelJobFenceRegistry.UpdateFence(chunk, meshingJob);
					ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(chunk, true);
					batchHandle = JobHandle.CombineDependencies(batchHandle, meshingJob);
				}

				JobHandle.ScheduleBatchedJobs();

				// Store batch fence on grid and set commit payload (disabled until ready)
				VoxelJobFenceRegistry.UpdateFence(grid, batchHandle);
				var evt = new RollingGridCommitEvent
				{
					gridID = gridData.gridID,
					targetAnchorWorldChunk = stepAnchor,
					targetOriginWorld = (float3)stepAnchor * stride,
				};
				Log.Debug(
					"[RGOrch] Commit payload: gid={gid} anchor={anchor} originWorld={origin}",
					evt.gridID,
					evt.targetAnchorWorldChunk,
					evt.targetOriginWorld
				);
				if (!HasComponent<RollingGridCommitEvent>(grid))
					ecb.AddComponent<RollingGridCommitEvent>(grid);
				ecb.SetComponent(grid, evt);
				ecb.SetComponentEnabled<RollingGridCommitEvent>(grid, false);

				// Consume request
				ecb.SetComponentEnabled<RollingGridMoveRequest>(grid, false);
			}

			// Poll active batches and raise commit event when ready
			var readyQuery = QueryBuilder()
				.WithAll<
					NativeVoxelGrid,
					RollingGridConfig,
					RollingGridBatchActive,
					RollingGridCommitEvent
				>()
				.WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
				.Build();
			using var readyGrids = readyQuery.ToEntityArray(Allocator.Temp);
			for (var i = 0; i < readyGrids.Length; i++)
			{
				var grid = readyGrids[i];
				if (VoxelJobFenceRegistry.TryComplete(grid))
				{
					ecb.SetComponentEnabled<RollingGridCommitEvent>(grid, true);
					var g = GetComponent<NativeVoxelGrid>(grid);
					var ev = GetComponent<RollingGridCommitEvent>(grid);
					Log.Debug(
						"[RGOrch] Commit ready: gid={gid} enabling event, originWorld={origin}",
						g.gridID,
						ev.targetOriginWorld
					);
				}
			}
		}

		[BurstCompile]
		public void OnDestroy(ref SystemState state) { }
	}
}
