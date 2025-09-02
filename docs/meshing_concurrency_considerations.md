## Meshing Concurrency and Per‑Mesh Job Fencing

### Objective
Guarantee correct, sync‑free sequencing between writers (stamps/procedural) that modify voxel SDF/material data and readers (meshing) that consume it, on a per‑mesh (chunk or singular mesh) basis, while allowing stamps to affect multiple chunks safely.

### Core Idea: Per‑Mesh Job Fence
- Track a per‑mesh job fence (JobHandle) on each entity that owns voxel data (shared path for chunks and singular meshes via `NativeVoxelMesh`).
- Writers and readers chain their jobs onto this fence using `JobHandle.CombineDependencies`, never scheduling concurrent read/write against the same mesh.
- Systems schedule on the managing thread only; call `JobHandle.ScheduleBatchedJobs()` after batching.

Recommended storage
- Primary: a system‑owned `NativeParallelHashMap<Entity, JobHandle>` (Fence Registry) keyed by entity. Access only from systems (not inside Burst jobs).
- Alternative: extend `NativeVoxelMesh` with a non‑serialized fence. This can couple system‑level dependencies via type access and is not the default recommendation.

### Concrete Implementation: VoxelJobFenceRegistry
- Implemented as `VoxelJobFenceRegistry` backed by a `SharedStatic<NativeParallelHashMap<Entity, JobHandle>>`.
- Lifecycle is managed by `VoxelJobFenceRegistrySystem` (initialized on create, disposed on destroy; runs early in `SimulationSystemGroup`).
- Public API (systems/main thread only):
  - `Initialize(int capacity)` / `Dispose()` / `IsCreated`
  - `Get(Entity)` and `Tail(Entity)` — return current fence or `default` if none
  - `Update(Entity, JobHandle)` — replace entity’s fence with provided handle
  - `Reset(Entity)` — remove fence entry
  - `CompleteAndReset(Entity)` — `Complete()` current fence and clear it
  - `TryComplete(Entity)` — if fence is `default` or already completed, call `Complete()`, clear entry, and return `true`; otherwise return `false`

Notes
- Do not access the registry from inside Burst jobs. Read/update it only in systems when scheduling or at managed boundaries.
- Use `Tail(e)` as the dependency source when scheduling, and immediately `Update(e, scheduledHandle)` with the returned handle.

### Writer Phase (Stamps / Procedural)
Systems: `VoxelStampSystem`, `ProceduralVoxelGenerationSystem`
1) Gather all affected meshes.
2) For each affected mesh entity `e`, read its current fence `pre = fence[e]`.
3) Schedule write jobs (SDF/material modifications) with dependency `pre`.
4) Update that mesh’s fence to include the scheduled write job(s): `fence[e] = JobHandle.CombineDependencies(fence[e], writeJob)`.
5) Optionally enable `NeedsRemesh` immediately; the mesher will still respect the fence.
6) Call `JobHandle.ScheduleBatchedJobs()`; do not assign `state.Dependency` unless recording ECB from jobs requires it.

Notes
- Destroying stamp entities at ECB playback is safe: job data is captured at schedule time.
- Add `[UpdateBefore(typeof(VoxelMeshingSystem))]` (or the specific meshing schedule system) to ensure all writer schedules occur before meshing schedules.

Concrete API usage (writers)
```csharp
// per affected mesh entity e
var pre = Voxels.Core.Concurrency.VoxelJobFenceRegistry.Tail(e);
var writeJob = new WriteSdfJob { /*...*/ }.Schedule(pre);
// If multiple jobs are scheduled, combine them before updating
Voxels.Core.Concurrency.VoxelJobFenceRegistry.Update(e, writeJob);
ecb.SetComponentEnabled<NeedsRemesh>(e, true);
JobHandle.ScheduleBatchedJobs();
```

### Reader Phase (Meshing)
System: meshing scheduler (e.g., `NaiveSurfaceNetsScheduleSystem`)
1) Query meshes with `NeedsRemesh` enabled.
2) For each, take its current fence as the dependency for read (meshing) jobs.
3) Schedule meshing jobs (read volume) with that dependency; update the fence to the returned handle.
4) Enable `NeedsManagedMeshUpdate` and disable `NeedsRemesh` via ECB.
5) Do not assign `state.Dependency`; call `JobHandle.ScheduleBatchedJobs()` and rely on per‑entity fences. Complete at the managed apply boundary.
Concrete API usage (readers)
```csharp
// per mesh entity e with NeedsRemesh
var pre = Voxels.Core.Concurrency.VoxelJobFenceRegistry.Tail(e);
var meshJob = new MeshReadJob { /*...*/ }.Schedule(pre);
Voxels.Core.Concurrency.VoxelJobFenceRegistry.Update(e, meshJob);
ecb.SetComponentEnabled<NeedsRemesh>(e, false);
ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(e, true);
JobHandle.ScheduleBatchedJobs();
```


### Multi‑Chunk Stamps (Deferred/Optional)
- Current plan: schedule per entity using each entity’s own fence without pre‑merging across many chunks.
- Future consideration: pre‑merge fences across all affected chunks to create a single logical write moment for large brushes. Revisit based on profiling and visual uniformity needs.

### ECB Usage and Sync‑Free Behavior
- Structural/tag changes (e.g., toggling `NeedsRemesh`, `NeedsManagedMeshUpdate`, queuing cleanups) are recorded via ECB (ParallelWriter from jobs or main thread) and played back after the system’s dependency chain.
- No `.Complete()` in normal flow; rely on per‑mesh fences for correctness. Use `.Complete()` only at boundaries that must observe results synchronously (e.g., managed mesh apply, rare debugging or shutdown).
- Managed boundary: complete the entity’s fence before applying meshes on the main thread, then reset that entity’s fence to `default` to avoid unbounded chains.
- If recording structural changes from jobs via `ECB.ParallelWriter`, set the ECB system’s dependency accordingly; otherwise avoid assigning `state.Dependency`.

Managed boundary (apply) — concrete API
```csharp
// Before uploading to Mesh/MeshCollider on main thread
Voxels.Core.Concurrency.VoxelJobFenceRegistry.CompleteAndReset(e);
// ...apply mesh data...
```

### Ordering and Safety
- Writers must be scheduled before readers for the same frame: `[UpdateBefore(typeof(…Meshing…))]` on both stamp and procedural systems.
- Mesher schedules jobs that read volume data with dependency on the mesh’s current fence.
- Writers schedule jobs that write volume data with dependency on the mesh’s current fence.
- Never schedule read & write concurrently for the same mesh/fence.

### Failure Modes to Avoid
- Scheduling a writer against a fence that already includes pending readers for the same mesh (or vice‑versa). Always chain onto the latest fence value.
- Using entity tags as the only gate. Tags indicate intent; the job fence guarantees ordering.
- Forgetting to update each affected mesh’s fence after scheduling jobs.

### Minimal Pseudocode Pattern
Writers (stamp/procedural):
```csharp
// per affected mesh entity e
var pre = meshFence[e];
var writeJob = new WriteSdfJob { /*...*/ }.Schedule(pre);
meshFence[e] = JobHandle.CombineDependencies(meshFence[e], writeJob);
ecb.SetComponentEnabled<NeedsRemesh>(e, true);
```

Mesher:
```csharp
// per mesh entity e with NeedsRemesh
var pre = meshFence[e];
var meshJob = new MeshReadJob { /*...*/ }.Schedule(pre);
meshFence[e] = JobHandle.CombineDependencies(meshFence[e], meshJob);
ecb.SetComponentEnabled<NeedsRemesh>(e, false);
ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(e, true);
```

Multi‑chunk stamp:
```csharp
var pre = JobHandle.CombineDependencies(fencesOfAllChunks);
foreach (var c in chunks)
{
    var write = new WriteSdfJob { chunk = c /*...*/ }.Schedule(pre);
    meshFence[c] = JobHandle.CombineDependencies(meshFence[c], write);
    ecb.SetComponentEnabled<NeedsRemesh>(c, true);
}
```

### Checklist
- Per‑mesh fence exists and is updated on every schedule.
- Writers/Readers always chain to the current fence; never parallelize R/W for the same mesh.
- Stamps update each affected mesh’s fence individually; no cross‑entity pre‑merge in the current plan.
- Systems generally do not assign `state.Dependency`; call `ScheduleBatchedJobs()`. Assign dependencies only when recording ECB from jobs.
- Ordering: Writers `[UpdateBefore]` readers; readers tolerate `NeedsRemesh` arriving early via fence chaining.

Implementation status
- Fence registry lives in `VoxelJobFenceRegistry` with lifecycle handled by `VoxelJobFenceRegistrySystem` (Default + Editor worlds). Use `Tail/Update/CompleteAndReset/TryComplete` as shown above.

### System Groups, Dependencies, and ECB Playback
- `state.Dependency` represents a system’s incoming deps and any jobs scheduled within it. You only need to assign it if:
  - Another system (or an ECB playback) must wait on the work you scheduled, or
  - You want the dependency analysis to propagate your jobs to subsequent systems in the group.
- ECB playback happens on the main thread and uses its system’s dependency to ensure recorded commands are safe to apply. If your tag toggles/structural changes must wait for your jobs, set the ECB system’s dependency (or `state.Dependency` when appropriate). Otherwise, rely on per‑entity fences.
- System groups (Initialization/Simulation) do not implicitly call `Complete()` on all jobs each frame. Jobs can remain in flight until a consumer introduces a sync point (e.g., ECB playback, main‑thread access, or explicit `.Complete()`).
- Before `OnUpdate`, a system’s `Dependency` reflects incoming deps from prior systems in the group; you can combine and reschedule as needed.

### Tests
- High‑level orchestration (Edit/PlayMode):
  - Stamping enables `NeedsRemesh` on affected entities and advances their fences.
  - Meshing respects fences, disables `NeedsRemesh`, enables `NeedsManagedMeshUpdate`.
  - Managed apply completes fences and clears them; mesh and collider attachments update.

### Performance & Stress
- Use `com.unity.test-framework.performance` to measure throughput and frame stability:
  - Measure frames during continuous stamping + meshing bursts.
  - Sample relevant profiler markers (e.g., meshing scheduler, upload, apply).
  - Example skeleton:

```csharp
using NUnit.Framework;
using System.Collections;
using UnityEngine.TestTools;
using Unity.PerformanceTesting;

[UnityTest, Performance]
public IEnumerator Voxel_Meshing_Stress_Frames()
{
    // Arrange: create several voxel meshes and start periodic stamps
    yield return Measure.Frames()
        .WarmupCount(5)
        .MeasurementCount(60)
        .Run();
}
```

References
- Dependency property semantics (incoming deps and use): see “Job Dependencies / Dependency property” and “Before OnUpdate() Dependency reflects incoming dependencies”.
- ECB playback runs on main thread and waits on dependencies: see “EntityCommandBuffer playback”.
- Job dependency management and avoiding unneeded dependencies: “Scheduling jobs / dependencies”.
- Performance testing API: “Unity Performance Testing – Measure.Frames/Measure.Method/ProfilerMarkers”.

Manual Check
- Packages: Unity.Entities, Unity.Jobs, Unity.Burst, com.unity.test-framework.performance
- Topics reviewed: Systems `Dependency`, ECB playback, manual dependency combining, Burst scheduling and managed boundaries, performance testing APIs
- Key guidance applied:
  - Avoid unneeded `Dependency` propagation; prefer explicit per‑entity fences
  - Assign dependencies only for ECB recorded from jobs; otherwise rely on fences
  - Complete at managed boundary (mesh apply); keep jobs in flight across frames


