## Unity Implementation Plan — Architecture-Aligned (C# + Burst/Jobs)

### Purpose
- Align the Unity plan with the architecture in `voxel_plugin_architecture.md` (Domain → Application → Infrastructure) while keeping a TDD-first, background-scheduled (jobified) workflow.
- Break work into fine-grained steps with clear deliverables, acceptance criteria, and demos.

### Architectural Alignment (mapping)
- Domain layer: pure voxel model (dims, indexing, chunk storage, mesh data, SDF/CSG, noise). No Unity types.
- Application layer: services and queues (volume management, meshing queue, budgets). Deterministic orchestration.
- Infrastructure layer: Unity adapters (ECS components/resources/events, Burst jobs, Mesh/Collider conversion, materials/shaders, logging).

### Global Defaults
- Sampling grid per chunk: samples = (32,32,32), core = (30,30,30), apron = 1 on all faces.
- Materials: `u8` annotations; no reserved ids. Annotation id encoded in vertex color `r = id/255.0`.
- Scheduling: jobified background workers; main thread applies render mesh/colliders; debounce collider rebuilds.
- Tests: EditMode unit tests per step; local/CI via Unity Test Runner (invoke through `pnpm test`).

---

## Milestones

### Milestone 1 — Single volume with terrain noise, meshing, collision, vertex-color materials
- Goal: Spawn one voxel volume (grid = 1×1×1 chunks), populate via procedural noise, generate/render mesh with per-vertex material encoding, and attach a physics collider. All heavy work scheduled off the main thread.
- Demo: Scene showing a lit terrain block; turning debug material palette on shows distinct materials; collider walkable; frame time stable while remeshing.
- Acceptance:
  - Deterministic EditMode tests for indexing, storage, noise fill, empty/solid early-outs, vertex/material counts on synthetic cases.
  - Visual demo: mesh present and colored; collider matches mesh; no cracks; stable frame budget during initial remesh.

### Milestone 2 — Runtime editing (place/destroy) with dirty → remesh
- Brush operations modify interior voxels; materials follow sign transitions; affected chunks enqueue and remesh within budget.

### Milestone 3 — Authoring (editor mode) and bake I/O
- In-editor stamps (sphere/box/cone) and mesh-to-SDF provider; deterministic bake read/write; hot-reload in dev.

### Milestone 4 — Scheduling/Performance/Diagnostics
- Budgets exposed/tweakable; telemetry counters; priority/ordering refinements; golden/regression tests.

---

## Milestone 1 — Fine-grained steps

### Step 0 — Domain: core grid/indexing primitives
- Deliverables
  - `ChunkDims { core, samples }` constants: core=(30,30,30), samples=(32,32,32), apron=1.
  - Linear index/de-index for samples: `i = x + sx*(y + sy*z)`; de-linearize helpers.
  - Extents/AABB helpers: `core_extent`, `sample_extent = core_extent.grow(1)`; world transform helpers (uniform scale only).
- Tests (EditMode)
  - Round-trip index↔coords across boundaries; apron positions valid; off-by-one assertions.

### Step 1 — Domain: voxel chunk storage (SoA + apron)
- Deliverables
  - `VoxelChunk` with `NativeArray<float> sdf`, `NativeArray<byte> material` sized to `samples.len` (32³), allocator Persistent.
  - `Initialize(fillSdf, defaultMaterial)`; `Dispose` safety; interior-authoritative coordinates [1..30] per axis.
- Tests
  - Array sizes/strides; fill and default material value checks; interior-only write validation.

### Step 2 — Domain: noise/population utilities
- Deliverables
  - Procedural noise filler (e.g., layered Perlin/Simplex or value noise) producing signed SDF terrain; threshold defines surface.
  - Authoring writes material annotations (e.g., from palette/height); the mesher only reads annotations.
- Tests
  - Deterministic noise (fixed seed) yields stable count of negative samples; small toy case snapshot check (checksum of sdf/material arrays).

### Step 3 — Application: VolumeManagementService (minimal)
- Deliverables
  - `VolumeConfig { chunkCoreDims, gridDims=(1,1,1), origin }` and volume creation.
  - `SeedVolumeWithNoise(config, seed)` populates chunk storage using Step 2 utilities.
  - Returns list of chunk coordinates needing initial remesh.
- Tests
  - 1×1×1 volume creation produces sample arrays sized correctly and negative/positive distributions.

### Step 4 — Infrastructure: ECS scaffolding (components/resources/events)
- Deliverables
  - Components: `VoxelVolumeMarker`, `VoxelChunkComponent{ coords }`, `VoxelChunkData{ chunk }`, `NeedsRemesh`.
  - Resources: `VoxelVolumeConfig`, `MeshingBudget { maxChunksPerFrame, timeSliceMs }`.
  - Events: `MeshReady { entity, mesh }` (Unity `Mesh` handle in infra; `MeshData` in domain tests).
- Tests
  - Basic world bootstrap and component attachment in EditMode; `NeedsRemesh` flag round-trip.

### Step 5 — Infrastructure: spawn systems (Boot)
- Deliverables
  - Startup system spawns a volume entity and one chunk child; attaches `VoxelChunkData` with initialized `VoxelChunk` storage; marks `NeedsRemesh`.
  - Optional: draw gizmos for sample/core extents in editor.
- Tests
  - Scene bootstrap spawns expected hierarchy; chunk has allocated storage; `NeedsRemesh` present.

### Step 6 — Infrastructure: Surface Nets mesher job (adapter)
- Deliverables
  - Burst job operating on `sdf`+apron; early-out when all positive or all negative (including apron) → no mesh.
  - Generate vertices/normals/indices for core cells; per-vertex material annotation chosen from 8 corners by minimal |sdf|; no special id preference.
  - Output buffer struct with positions, normals, indices, and parallel `materialIds`.
- Tests
  - Synthetic SDFs (sphere/plane) produce stable vertex counts and expected material ids; empty/solid cases skip.

### Step 7 — Application: remesh scheduler (jobified)
- Deliverables
  - Global FIFO queue of chunk coords; `MeshingBudget` controls `maxChunksPerFrame` and `timeSliceMs`.
  - Pop until budget/time slice consumed; schedule Burst meshing jobs; marshal results back; emit `MeshReady` events.
  - Stable ordering inside a frame; work does not exceed budget.
- Tests
  - Budget adherence (count/time); stable order; no main-thread stalls beyond mesh apply.

### Step 8 — Infrastructure: mesh apply and rendering materials/shader hookup
- Deliverables
  - Convert buffer to Unity `Mesh` with `ATTRIBUTE_COLOR` inserted; `color.r = materialId/255.0`, g/b = 0, a = 1.
  - Renderer per chunk uses triplanar shader reading vertex color R as the rendering material id; optional `DEBUG_MAT_VIS` palette toggle.
  - Transform set from sample extent minimum (apron-inclusive origin) to align chunk world position.
- Tests
  - Material encode/decode unit test; scene renders without submesh splits; adjacent materials visible.

### Step 9 — Infrastructure: physics collider with debounce
- Deliverables
  - Convert render triangles to collider per chunk; debounce rebuilds (default one frame) to avoid thrash.
  - Broadphase hints via chunk AABB from core extent transformed to world.
- Tests
  - Collider appears when mesh is present; no repeated rebuilds within debounce window under forced remesh bursts.

### Step 10 — Diagnostics and minimal telemetry
- Deliverables
  - Counters: chunks enqueued/meshed per frame, queue length, mesh time p50/p95.
  - Logging hooks per project rules to aid budget tuning.
- Tests
  - Metrics report expected values in controlled test scenes.

---

## Subsequent milestones (outline)

### Milestone 2 — Runtime editing (place/destroy)
- Steps
  - Brush ops: compute affected core cells; apply `min(s,b)` for place and `max(s,-b)` for destroy; update material on sign flips.
  - Dirty mapping: mark chunks overlapping the brush AABB + apron; enqueue deduped.
  - Tests: sign-transition material rules; edits confined to affected chunks.

### Milestone 3 — Authoring and bake I/O (editor)
- Steps
  - Editor-only stamps (sphere/box/cone + smooth-k CSG) producing deterministic SDF/material.
  - Mesh-to-SDF provider: narrow band ±3 voxels; winding-number default; flood-fill fallback optional later.
  - Bake format write/read; hot-reload in dev swaps storage and enqueues remesh.
  - Tests: golden scene bake/load equal; hot-reload triggers remesh.

### Milestone 4 — Performance and diagnostics
- Steps
  - Budget inspector exposure; time-sliced scheduling; optional camera-aware prioritization.
  - Telemetry dashboards; determinism checks; golden vertex/index hash tests.
  - Tests: queue drains under bursts; frame budgets respected; stable golden hashes.

---

## Risks and mitigations
- Off-by-one at apron/core boundaries → exhaustive boundary tests; interior-only write guards.
- Material selection inconsistencies → deterministic nearest-|sdf| policy; debug palette visualization.
- Main-thread stalls on mesh apply → strict background scheduling and time-sliced drain; pre-allocated buffers.
- Cross-CPU non-determinism → fixed seeds; avoid FMA-sensitive math; verify with golden counts/hashes.

## TDD cadence
- Write minimal EditMode tests before each step; keep steps ≤ ~2 files and ≤ ~200 LOC; compile/test after every step.
- Run local tests consistently via `pnpm test` to surface Unity compiler/test errors reliably.