## Simplified Meshing Pipeline (Test‑Friendly, Fully Async)

### Goals
- Make the pipeline easy to reason about and test deterministically
- Keep fully asynchronous background processing for procedural + meshing jobs
- Preserve atomic visual presentation for rolling grids, but drastically simplify apply logic
- Accept small, bounded overhead (staging/back mesh) in exchange for clarity and stability

### Pain Points in Current Flow (Why It’s Hard Today)
- Apply is cross‑cut by many gates: per‑entity job fences, rolling commit/batch state, budgets, and enableable tags
- Tests drive systems out of the usual player loop; counters can add/sub within a single frame when sync mode is on, making progress hard to observe
- Hidden global state (SharedStatic) and ordering make determinism brittle across test worlds
- Strict success criteria (e.g., FullyMeshedEvent) require every chunk to apply, which often conflicts with rolling atomicity gating

### Proposed Architecture

High‑level idea: always apply into a staging/back mesh as soon as a chunk’s fence completes. Presentation is separated from apply:
- Non‑rolling grids: assign MeshFilter/MeshCollider immediately (simple path)
- Rolling grids: mark chunks as BackMeshReady; do not assign immediately. Present all staged meshes in a single atomic swap during the grid’s commit

This separates concerns:
- Jobs (procedural, meshing/upload) remain fully async
- Managed apply becomes deterministic and cheap (fence → back mesh)
- Rolling orchestration is the only place that performs atomic swap; apply never gates on commit/batch anymore

### Flow Diagram (Simplified)

```
flowchart TD
    A[Procedural Job(s)] -->|fence complete| B
    B[Meshing/Upload Job(s)] -->|fence complete| C
    C[Managed Apply]
    C -->|non-rolling| D[Assign MeshFilter.sharedMesh now]
    C -->|rolling| E[Mark BackMeshReady]
    E --> F[Rolling Commit]
    F -->|atomic swap| G[Assign MeshFilter for all BackMeshReady]
```

### Data/Components
- New: `BackMeshReady : IComponentData, IEnableableComponent`
  - Enabled on chunks whose back/staging mesh has been applied and awaits presentation in a rolling commit
- Optional config: `VoxelProjectConfiguration.simpleApply` (bool)
  - When true (e.g., in tests), treat all grids as non‑rolling for presentation; i.e., assign MeshFilter immediately and skip atomic swap

### Code Changes (Illustrative)

1) Managed apply: remove commit/batch gating; always apply when fence is ready

```csharp
// ManagedVoxelMeshingSystem.ApplyManagedMeshesForReadyEntities (key excerpt)
foreach (var (nvmRef, entity) in Query<RefRW<NativeVoxelMesh>>()
    .WithAll<NeedsManagedMeshUpdate>()
    .WithEntityAccess())
{
    // Complete fence (sync gate already present today)
    if (VoxelDebugging.RunJobsSynchronously)
        VoxelJobFenceRegistry.CompleteAndReset(entity);
    else if (!VoxelJobFenceRegistry.TryComplete(entity))
        continue;

    ref var nvm = ref nvmRef.ValueRW;
    if (nvm.meshing.meshData.Length == 0)
        continue;

    // Always apply into back/staging mesh
    nvm.ApplyMeshManaged();
    ecb.SetComponentEnabled<NeedsManagedMeshUpdate>(entity, false);

    // Decide presentation policy
    var gridIsRolling = false;
    if (EntityManager.HasComponent<NativeVoxelChunk>(entity))
    {
        var gid = EntityManager.GetComponentData<NativeVoxelChunk>(entity).gridID;
        // Look up rolling state of the owning grid (pseudo helper)
        gridIsRolling = GridIsRolling(gid);
    }

    var simpleApply = VoxelProjectConfiguration.Get().simpleApply;
    var presentNow = !gridIsRolling || simpleApply;

    if (presentNow)
    {
        // present immediately (non‑rolling or test/simple mode)
        AttachMeshFilterNow(entity, nvm.meshing.meshRef);
        AttachMeshColliderNow(entity, nvm.meshing.meshRef);
    }
    else
    {
        // rolling: stage for atomic commit
        if (!EntityManager.HasComponent<BackMeshReady>(entity))
            ecb.AddComponent<BackMeshReady>(entity);
        ecb.SetComponentEnabled<BackMeshReady>(entity, true);
    }
}
```

2) Rolling commit: swap in one frame

```csharp
// RollingGridOrchestratorSystem.ProcessCommitBatches (add swap pass)
// After enabling RollingGridCommitEvent and prior to clearing flags:

foreach (var grid in commitGrids)
{
    // Iterate LinkedEntityGroup children
    var leg = EntityManager.GetBuffer<LinkedEntityGroup>(grid);
    for (int i = 1; i < leg.Length; i++)
    {
        var chunk = leg[i].Value;
        if (!EntityManager.HasComponent<NativeVoxelChunk>(chunk))
            continue;
        if (!EntityManager.HasComponent<BackMeshReady>(chunk))
            continue;
        if (!EntityManager.IsComponentEnabled<BackMeshReady>(chunk))
            continue;

        // Fetch back mesh handle
        var nvm = EntityManager.GetComponentData<NativeVoxelMesh>(chunk);
        // Front <- Back: assign sharedMesh/meshCollider
        AssignMeshFilterNow(chunk, nvm.meshing.meshRef);
        AssignMeshColliderNow(chunk, nvm.meshing.meshRef);
        // Clear the staging flag
        ecb.SetComponentEnabled<BackMeshReady>(chunk, false);
    }

    // Any transform/anchor updates keep as‑is
    // Clear RollingGridBatchActive and RollingGridCommitEvent as before
}
```

3) Minimal new component

```csharp
public struct BackMeshReady : IComponentData, IEnableableComponent {}
```

4) Optional config flag

```csharp
// VoxelProjectConfiguration
public struct VoxelProjectConfig
{
    public int fenceRegistryCapacity;
    public bool simpleApply; // default: false
}
```

### Test Strategy
- Non‑rolling convergence (MonoBehaviour‑only test):
  - Create `VoxelMeshGrid` 1×1×1 with `SimpleNoiseVoxelGenerator`
  - Enable `RunJobsSynchronously = true` and `simpleApply = true`
  - Step initialization and then procedural → meshing → managed apply a handful of frames
  - Assert a child `MeshFilter` has a mesh and that `GlobalMeshingCounters` converge to 0

- Rolling convergence (atomic swap):
  - Create a rolling grid with a few chunks, mesh them with `simpleApply = false`
  - Step frames until chunks have `BackMeshReady` enabled
  - Trigger a rolling commit and assert all children now have `MeshFilter.sharedMesh` assigned in the swap frame

### Trade‑offs
- Slight memory overhead for staging/back mesh between apply and swap
- Presentation is deferred for rolling until commit (expected); tests can enable `simpleApply` to present immediately
- Removes apply‑time gating and budgets, centralizing atomicity concerns in the commit swap only

### Migration Steps
1) Add `BackMeshReady` component
2) Update `ManagedVoxelMeshingSystem` to always apply and choose presentation policy
3) Update rolling commit path to swap all `BackMeshReady` chunks in one frame
4) Add optional `simpleApply` to `VoxelProjectConfiguration`
5) Update tests:
   - Non‑rolling tests use `simpleApply = true`
   - Rolling tests assert `BackMeshReady` before swap, and correct front mesh after swap

### Expected Outcome
- Deterministic convergence in tests; apply never blocks on rolling gates
- Clear separation of responsibilities: jobs → staging apply → (optional) atomic swap
- Maintains full async background processing and rolling atomicity with much simpler control flow


