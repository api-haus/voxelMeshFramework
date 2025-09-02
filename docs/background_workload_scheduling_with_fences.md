## Background workload scheduling with fences (Entities 1.3)

### Summary
- **Finding**: In Entities 1.3, accessing component data via `SystemAPI.Query` / `foreach`, `GetComponent*`, or other main-thread reads/writes completes relevant job dependencies for safety. Therefore, purely "background" system-group updates are not possible; only their jobs run in the background.
- **Resolution**: Schedule voxel workloads using our own per-entity fences, and keep Entities usage limited to memory ownership and work tracking via tags. Avoid main-thread data access that forces synchronization, except for intentional sync points (e.g., ECB playback).

### Why queries complete dependencies
- SystemAPI source generators automatically insert `CompleteDependencyBeforeRO/RW` around `foreach` iterations and direct component access to guarantee safety.
- Direct `EntityManager` reads/writes and ECB playback also force completion.
- Net effect: Any main-thread access to components (beyond recording commands) can synchronize with outstanding jobs.
  - See Entities docs: Systems → SystemAPI query semantics; Scheduling jobs and dependencies; EntityCommandBufferSystem.

### Design principles for voxel workloads
- **Use job fences to decouple from entity synchronization**: chain background jobs per-entity and store their tail `JobHandle` in a registry.
- **Entities as control-plane only**:
  - Memory ownership (persistent containers in components)
  - Work tracking via tags (`NeedsProceduralUpdate`, `NeedsRemesh`, `NeedsManagedMeshUpdate`, etc.)
- **No forced sync for tag updates**: record tag toggles through ECB; let playback be the explicit sync point at group end.
- **Fence-first access rule**: before reading/writing component-backed native memory on the main thread, require the entity’s fence to be complete.

### Canonical scheduling pattern
1) Iterate candidate entities (prefer tag-only filters).
2) For each entity `E`:
   - If `!TryComplete(E)`: skip this frame (work still in-flight)
   - `pre = Get(E)` to fetch prior tail
   - Schedule background job chain using `pre`
   - `Update(E, newTail)`
   - Record tag changes via ECB
3) Call `JobHandle.ScheduleBatchedJobs()` to flush scheduling.

Example (simplified)
```csharp
// Pseudocode illustrating the pattern
foreach (var (/* minimal refs */, entity) in Query<...>()
           .WithAll<NeedsWork>()
           .WithEntityAccess())
{
  if (!VoxelJobFenceRegistry.TryComplete(entity))
    continue; // Defer until prior work is done

  var pre = VoxelJobFenceRegistry.Get(entity);
  var job = MyWork.Schedule(/* inputs */ , pre);
  VoxelJobFenceRegistry.Update(entity, job);

  ecb.SetComponentEnabled<NeedsWork>(entity, false);
  ecb.SetComponentEnabled<NextStageTag>(entity, true);
}

JobHandle.ScheduleBatchedJobs();
```

### Existing references in this repository
- Fence registry and system
```1:101:Packages/com.voxelmeshframework/Core/Concurrency/VoxelJobFenceRegistrySystem.cs
// ... existing code ...
```

- Stamping (fence-checked writes to volume, tag handoff)
```20:113:Packages/com.voxelmeshframework/Core/Stamps/VoxelStampSystem.cs
// ... existing code ...
```

- Meshing (fence-checked reads, background meshing + upload, tag handoff)
```15:236:Packages/com.voxelmeshframework/Core/Meshing/Systems/VoxelMeshingSystem.cs
// ... existing code ...
```

- Procedural generation (fence-checked generation, tag handoff)
```17:72:Packages/com.voxelmeshframework/Core/Procedural/ProceduralVoxelGenerationSystem.cs
// ... existing code ...
```

### Practical guidelines
- **Minimize main-thread component access**
  - Prefer tag-only queries to discover work; when accessing component data, do it only after `TryComplete(entity)`.
  - Avoid `.Run()` and ad-hoc `EntityManager` reads/writes inside update loops.
- **Use ECB for structural/tag changes**
  - Record tag toggles; accept ECB playback as the explicit sync point.
- **Keep job chains fully asynchronous**
  - Pass `pre` into each scheduled stage; return the final `JobHandle` as the new tail.
  - Call `JobHandle.ScheduleBatchedJobs()` to reduce scheduler overhead.
- **Do not rely on group off-thread execution**
  - Groups/systems always tick on the main thread; only their jobs are background.

### FAQ
- **Can a fence survive between frames?** Yes. Fences are stored per-entity and persist; next-frame scheduling composes onto the previous tail.
- **Who completes dependencies?** Main-thread access (SystemAPI/EntityManager), ECB playback, explicit `Complete()`, and the job scheduler via declared dependencies.
- **Do tag toggles force sync?** Recording to an ECB does not; playback does, at a known point (e.g., EndSimulation ECB).

### Outcome
- Background voxel workloads proceed independently of entity data synchronization, with explicit, localized sync only at fence checks and ECB playback. Entities remain the control-plane for memory and work tagging; fences orchestrate safe, persistent background execution.


