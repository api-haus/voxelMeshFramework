### Single Rolling Grid (Toroidal) — Implementation Plan

#### 1) Scope and assumptions
- Implement a single rolling grid that maintains a fixed pool of chunk slots and follows the player.
- Presentation is GameObject-backed; transforms are driven from GameObjects (not ECS → GO).
- Visual updates are atomic per relocation batch (no partial swaps).
- Seams remain crack‑free; stamps may arrive during relocation.

#### 2) Data and components
- Reuse existing:
  - `NativeVoxelGrid { gridID, voxelSize, bounds }`
  - `NativeVoxelChunk { coord:int3, gridID:int }`
- New (ECS):
  - `RollingGridCommitEvent : IEnableableComponent { gridID:int, targetAnchorWorldChunk:int3, targetOriginWorld:float3 }`
- Orchestrator state (managed, per grid):
  - `anchorWorldChunk:int3`, `dims:int3`
  - Slot maps: `slot → worldChunk`, `worldChunk → slot`
  - In‑flight counters: `generateInflight, apronInflight, meshInflight, stitchInflight, stampInflight`
  - Aggregate `JobHandle batchHandle`

#### 3) Systems
- RollingGridOrchestratorSystem (new)
  - Detect movement ≥ one chunk along any axis; compute `movedChunks`.
  - Update `anchorWorldChunk += movedChunks` (logical only; presentation unchanged).
  - Compute entering and leaving one-row slabs for the moved axis (e.g., +X: entering `slot.x==dims.x-1`, leaving `slot.x==0`).
  - For each entering slot (one-row slab only):
    1) Generate: schedule procedural fill (or load) into slot `nvm.volume`; ++/-- `generateInflight`.
    2) Apron sync: `CopySharedOverlap` with neighbors; ++/-- `apronInflight`.
    3) Meshing: mesh to staging `MeshData`; ++/-- `meshInflight`.
    4) Seam (optional): harmonization sweeps on seam faces; ++/-- `stitchInflight`.
  - For runtime stamps while batch active:
    - Only interior chunks are editable. Schedule stamp → apron → mesh (staging) for those chunks; ++/-- `stampInflight`, `apronInflight`, `meshInflight`.
  - Aggregate dependencies into `batchHandle`.
  - Ready detection: when `batchHandle.IsCompleted` and all inflight counters are zero → enable `RollingGridCommitEvent` with `gridID`, `targetAnchorWorldChunk`, `targetOriginWorld`.
  - No versioning: movement input is clamped while a batch is active; start next relocation only after commit.

- ManagedVoxelMeshingSystem (extend only behavior, not API)
  - Keep per-entity path (applies when no commit event for the grid is present).
  - When a `RollingGridCommitEvent` is enabled for a grid:
    - Gather all chunks with `NativeVoxelChunk.gridID` equal to the committing grid.
    - For each chunk entity with `NeedsManagedMeshUpdate` and completed fence:
      - `ApplyAndDisposeWritableMeshData(staging, front)`; disable `NeedsManagedMeshUpdate`.
    - Move each chunk GameObject to its new origin; move the grid anchor GameObject to `targetOriginWorld`.
    - Disable the `RollingGridCommitEvent`.

#### 4) Job scheduling & counters
- Chain job phases by passing `JobHandle` dependencies; increment counters before scheduling and decrement in job completion callbacks or after completion joins.
- Batch Ready condition: `generateInflight == apronInflight == meshInflight == stitchInflight == stampInflight == 0 && batchHandle.IsCompleted`.

#### 5) Movement & slot selection
- Use toroidal mapping: `slot = Mod(worldChunk - anchorWorldChunk, dims)`; `origin = worldChunk * (EFFECTIVE_CHUNK_SIZE * voxelSize)`.
- Single one‑row slab (entering/leaving) remaps per move; apron sync ensures stitching with interior. Interior slots keep their mapping.

#### 6) Seams (single grid)
- Pre-mesh apron copy between adjacent chunks.
- During fairing: keep seam-aware neighbor graph and soft band constraints.
- Optional: seam harmonization pass over seam faces (1–2 sweeps) for equality.
- Optional: tangential quantization (`voxelSize/256`) on staging vertices for determinism.

#### 7) Stamps during relocation
- Always accepted on interior chunks only:
  - No batch: stamp → apron → mesh → immediate front apply (existing flow).
  - Batch active: stamp routed to staging; front remains unchanged until commit.

#### 8) Presentation (atomic commit)
- Main thread only:
  - For all chunks of the committing grid: swap staging → front meshes.
  - Move chunk GameObjects to new origins.
  - Move grid anchor GameObject to target origin.
  - Disable commit event.

#### 9) Telemetry
- Record durations per phase (generate/apron/mesh/stitch/stamp) and batch wall time.
- Track counts of entering slots and staged stamps.

#### 10) Test plan
- Movement: +X/+Y/+Z one chunk at a time; ensure no partial visual change; atomic swap only.
- Seams: seam vertex equality across internal seams post-commit (ε ≤ 1e−5), no visible cracks.
- Stamps: apply stamps during relocation; verify they appear at commit and do not tear seams.
- Stability: repeated moves under heavy stamping; counters reach zero; events fire reliably.

#### 11) Rollout
1) Orchestrator scheduling + commit event + atomic swap (no harmonization).
2) Add seam harmonization and stamp replay.
3) Tune quantization step, budgets, and logging.


