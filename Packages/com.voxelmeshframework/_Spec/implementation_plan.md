## Implementation Plan (Agent-Oriented)

### High-level areas of implementation
- Core math/layout primitives
- Voxel storage (SoA + apron)
- Meshing (fast_surface_nets) and buffer → Bevy Mesh
- Bevy ECS plugin scaffolding (volumes/chunks as entities)
- Authoring (editor build: SDF shapes + mesh_to_sdf) and baking I/O
- Runtime editing (brush ops) and dirty → remesh mapping
- Remesh scheduler (budgets, prioritization, Rayon)
- Physics colliders (avian3d) and debounce
- Diagnostics, tests, and golden/regression suite
- Dev ergonomics (hot-reload, tunables, perf budgets)

### Agent execution constraints (global)
- Keep steps ≤ 2 files and ≤ ~200 LOC diff; compile and test after each step.
- Prefer stubbing APIs first; fill in small functions incrementally.
- Avoid broad refactors; evolve public APIs cautiously.
- Gate each step with cargo check/test and (where relevant) a small example.

## Area breakdown (scope → tasks → acceptance)

### Core math/layout primitives
- Scope
  - Grid indexing; chunk core vs sample dims; apron addressing; AABB utilities.
- Deliverables
  - `ChunkDims { core: UVec3, sample: UVec3 }`, `origin_cell: IVec3` helpers.
  - `linear_index(x,y,z, sample_dims)` and de-linearize (debug-checked).
  - ilattice `Extent` helpers: `core_extent(origin, core)`, `sample_extent(origin, core).padded(1)`.
  - Transform helpers: volume-local/world conversions (uniform scale only).
- Tasks
  - Implement functions with unit tests for boundary and apron math.
  - Add doc examples and assertions for off-by-one pitfalls.
- Acceptance
  - Property tests: apron indexing never OOB; core↔sample conversions round-trip.

### Voxel storage (SoA + apron)
- Scope
  - `VoxelStorage { sdf: Box<[f32]>, mat: Box<[u8]> }` sized by `sample_dims`.
- Deliverables
  - Constructor for fixed 32×32×32 sampling; `fill_default(sdf_val, mat_id)`.
  - No reserved ids; material byte validation.
  - (Stub) `copy_apron_from_neighbors()` for future neighbor refresh.
- Tasks
  - Allocate `(Cx+2)*(Cy+2)*(Cz+2)` elements; align with `ConstShape3u32::SIZE`.
  - Quick size tests against `ndshape` constants.
- Acceptance
  - Unit tests pass; size math exactly matches compile-time shapes.

### Meshing (fast_surface_nets) and buffer → Bevy Mesh
- Scope
  - Integrate `fast_surface_nets` with `ndshape::ConstShape3u32`.
- Deliverables
  - `remesh_chunk_fixed::<CX,CY,CZ>(sdf: &[f32]) -> Option<SurfaceNetsBuffer>`.
  - `remesh_chunk_dispatch(core_dims: UVec3)`; supported: 32³ only (+1 apron inside samples).
  - Early skip when all positive or all negative (including apron).
  - `buffer_to_meshes_per_material(...) -> HashMap<u8, Mesh>`; default triplanar (no UVs required).
- Tasks
  - Corner-based material selection (min |s| among 8 corners); majority per-triangle bucketing.
  - Optional planar-UV path for testing (behind feature flag).
- Acceptance
  - Synthetic sphere SDF produces stable triangle count; empty/solid chunks skip mesh.

### Bevy ECS plugin scaffolding
- Scope
  - `VoxelPlugin`, system sets, events, entity hierarchy.
- Deliverables
  - Volume entity (`VoxelVolume`) with `SpatialBundle`.
  - Chunk entities (`VoxelChunk`, `VoxelStorage`) parented to volume.
  - Events: `VoxelEditEvent`, `ChunkRemeshResult`.
  - System sets: Authoring, Editing, Schedule, ApplyMeshes, Physics (chained).
- Tasks
  - `spawn_volume_chunks()` using `chunk_core_dims` and `origin_cell` iteration.
  - Minimal `remesh_scheduler_frame` stub and event drain.
- Acceptance
  - Example scene spawns volumes/chunks; systems run without panics.

### Authoring (visual editor build + SDF shapes/mesh_to_sdf) and baking I/O
- Scope
  - Visual editor workflow (Yoleck-like): in-app editing of authoring components, scene persistence via the editor’s format; no RON scene path. Editor features are compiled only in the developer/editor build, not in shipping builds.
  - Voxelization; bake read/write.
- Deliverables
  - Shapes: sphere, box, cone; ops: union/intersect/subtract (+ smooth k).
  - Mesh-SDF provider: narrow band ±3 voxels; sign method (winding default; flood-fill fallback).
  - Bake format: header + LZ4 body; optional CRC32; per-volume directory structure.
- Tasks
  - Integrate editor-mode plugin to allow creating/editing authoring entities in-app.
  - Per-chunk voxelization: cull contributors by AABB+apron; compose SDF/material per sample.
  - Material rule: choose contributor with smallest |s|; tie by priority else last-writer.
  - `write_vxb/read_vxb`; file watcher for hot-reload (dev only).
- Acceptance
  - Deterministic bake of a golden scene; reload replaces storage and triggers remesh.

### Runtime editing (brush ops) and dirty → remesh mapping
- Scope
  - Shape-cast place/destroy; material sign-flip rules.
- Deliverables
  - World→volume-local brush transform; affected cell range from brush AABB + apron.
  - Per-voxel ops: `min(s,b)` (place), `max(s,-b)` (destroy) with material updates on sign change.
- Tasks
  - Apply to overlapping chunks only; mark dirty and enqueue with dedupe.
  - Track last-edit time for prioritization.
- Acceptance
  - Edits modify only affected chunks; material assignment on sign transitions correct.

### Remesh scheduler (budgets, prioritization, Rayon)
- Scope
  - Global queue; camera-aware prioritization; budgets.
- Deliverables
  - `RemeshBudget { max_chunks_per_frame, time_slice_ms }` resource.
  - Priority: on-screen first → distance → last-edit timestamp.
  - Stable per-frame work ordering; Rayon job dispatch.
- Tasks
  - Lock-free MPSC or Bevy channel; pop until budget spent; spawn jobs; emit results/event.
  - Metrics: queue length, chunks processed, time spent.
- Acceptance
  - Under burst edits, CPU frame time remains within budget; queue drains over frames.

### Physics colliders (avian3d) and debounce
- Scope
  - Build colliders from merged triangles; debounce updates.
- Deliverables
  - Debounce (default 1 frame); drop intermediate updates within window.
  - Broadphase hints via chunk core AABB transformed to world.
- Tasks
  - Convert per-material meshes to triangle iterator; construct avian3d collider; attach to chunk.
  - Clear `NeedsCollider`; schedule follow-up only if new mesh arrives after debounce.
- Acceptance
  - Collider updates lag mesh by ≤ 66 ms typical; no thrash under rapid edits.

### Diagnostics, tests, and golden/regression suite
- Scope
  - Telemetry counters; crack tests; determinism checks.
- Deliverables
  - Counters: chunks meshed/edited, queue length, mesh time p50/p95.
  - Golden scenes: bake + mesh vertex/index hashes (stable on same OS/CPU).
  - Crack sweep test across chunk borders; apron correctness tests.
- Tasks
  - Unit tests per module; headless integration test that authors, remeshes, edits.
  - Determinism test with fixed thread pool size.
- Acceptance
  - CI green; golden hashes stable; crack tests pass; no seams across chunk boundaries.

### Dev ergonomics (hot-reload, tunables, perf budgets)
- Scope
  - Tunables resource; file watch; example scene.
- Deliverables
  - `RemeshBudget` exposed to bevy inspector in dev; live tweakable.
  - Material library: u8 → PBR handles; default triplanar shader hookup.
  - Example app: camera, light, volume(s), simple brush controls.
- Tasks
  - CLI/env flags for voxel size (dev).
  - Bake hot-reload swaps storage and enqueues remesh.
- Acceptance
  - Demo runs at target FPS; budgets adjustable at runtime; hot-reload works.

## Phase-by-phase execution plan (short-context friendly)

### Phase 0: Core primitives
- Files: `src/core/grid.rs`, `src/core/index.rs` (new)
- Work
  - Implement `ChunkDims`, index helpers, `Extent` utilities; unit tests.
- Exit
  - `cargo test` green; docs/examples for apron math.

### Phase 1: Voxel storage
- Files: `src/voxels/storage.rs` (new)
- Work
  - `VoxelStorage` alloc/reset; shape-checked sizes; material byte validated.
- Exit
  - Size tests pass; memory layout documented.

### Phase 2: Meshing
- Files: `src/meshing/surface_nets.rs`, `src/meshing/bevy_mesh.rs` (new)
- Work
  - `remesh_chunk_fixed` + dispatch + empty/solid skip; unit test with sphere SDF.
  - Buffer→Bevy Mesh conversion; per-material split; default triplanar.
- Exit
  - Example function returns non-empty mesh for sphere; skips empty/solid.

### Phase 3: Bevy plugin skeleton
- Files: `src/plugin/mod.rs` (new), `src/main.rs` (wire-up)
- Work
  - `VoxelPlugin`, sets, events; spawn volumes/chunks; no meshing.
- Exit
  - App runs; hierarchy correct; no runtime errors.

### Phase 4: Scheduler + mesh apply
- Files: `src/plugin/scheduler.rs`, `src/plugin/apply_mesh.rs`
- Work
  - Remesh queue + budgets; Rayon jobs; result drain; apply meshes on main thread.
- Exit
  - Visible meshes appear for initial SDF (e.g., authored sphere).

### Phase 5: Authoring + baking I/O
- Files: `src/authoring/{components.rs, voxelize.rs, bake.rs}`
- Work
  - Shapes + CSG + per-chunk voxelization; bake write/read; hot-reload (dev).
- Exit
  - Golden scene bakes and loads identically; mesh matches.

### Phase 6: Editing (runtime brushes)
- Files: `src/plugin/editing.rs`
- Work
  - Place/destroy ops; brush AABB→chunk mapping; material sign-flip rules.
- Exit
  - Edits update meshes within target latency; only affected chunks remesh.

### Phase 7: Physics colliders
- Files: `src/plugin/physics.rs`
- Work
  - Build colliders from render mesh; debounce updates; broadphase hints.
- Exit
  - Colliders update ≤ 66 ms after mesh; no thrash.

### Phase 8: Diagnostics & tests
- Files: `src/diagnostics.rs`, `tests/{golden.rs, cracks.rs}`
- Work
  - Counters; golden hashes; crack tests; determinism test.
- Exit
  - CI suite passes; no seams; stable outputs.

### Phase 9: Dev ergonomics
- Files: `examples/demo.rs`, `README.md`
- Work
  - Example app with controls; tunables exposed; perf budgets configurable.
- Exit
  - Demo showcases authoring, editing, meshing, and physics at target FPS.

## Risks & mitigations (for the agent)
- Large diffs cause context overflow
  - Mitigation: cap steps to ≤ 2 files and ≤ ~200 LOC; prefer additive code.
- Non-determinism across CPUs
  - Mitigation: fix thread pool size during authoring/tests; avoid FMA-sensitive math; hash meshes in tests.
- Cracks at boundaries
  - Mitigation: strict apron rules; unit tests for border cells; crack sweep tests.
- Mesh material splits inflating vertex count
  - Mitigation: start with duplicate-per-material; optimize later with remapping if needed.

## Checklists
- Each phase
  - cargo check/test pass
  - Minimal docs updated
  - Telemetry counters (if applicable)
  - Demo or test exercising new code path

## Out-of-scope (v1)
- LOD, streaming, sparse compression, GPU meshing, non-uniform scale.
- Editor in shipping builds; Android/WebGL/WASM/other platforms (focus on Windows/Linux/macOS/iOS only).
