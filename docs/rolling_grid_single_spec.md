### Single Rolling Grid (Toroidal) – Technical Specification

#### Goals
- Maintain a fixed-size pool of chunk slots (constant memory) that follows the player.
- Regenerate only newly “discovered” chunks and keep seams crack‑free.
- Perform all updates in the background; swap to new presentation atomically.

#### Definitions
- **Chunk**: 32³ voxel volume; world spacing `EFFECTIVE_CHUNK_SIZE * voxelSize`.
- **Grid dims**: number of slots in X,Y,Z (e.g., 16×8×16).
- **Anchor**: world chunk coordinate at the grid’s logical origin.

#### Player and editing constraints
- The player remains near the central chunk of the grid at all times (by design of the rolling grid).
- Editing is prohibited on edge chunks (the two-layer entering border and outer shell). Only interior chunks are editable.
- Define an editable band/window: all chunks with slot coords in `[2 .. dims-3]` per axis (leaving a 2-chunk safety border).

#### Data Model
- `NativeVoxelGrid`: `gridID`, `voxelSize`, `bounds` (world AABB for current coverage).
- Slots: array of `NativeVoxelMesh` (reused buffers per slot).
- Mapping:
  - `slot = Mod(worldChunk - anchorWorldChunk, dims)`
  - `worldChunk = anchorWorldChunk + Wrap(slot, dims)`
- Per-slot state: `worldChunk`, `volumeValid`, `frontMesh`, `backMeshData`.

#### Movement Trigger
- When player crosses `EFFECTIVE_CHUNK_SIZE * voxelSize` along any axis → compute `movedChunks`.
- If any component nonzero, begin relocation batch.

#### Batch Steps (Background)
1) Advance `anchorWorldChunk += movedChunks` (logical only; presentation unchanged).
2) Identify newly discovered slots via remap.
3) For each new slot:
   - Compute `origin` and `bounds` from `worldChunk`.
   - Schedule generator to fill `nvm.volume`.
4) After fills, run `CopySharedOverlap` for seam aprons with all 6 neighbors.
5) Mesh all valid slots into `backMeshData` (no front change yet).
6) Optional: seam harmonization pass across slot boundaries.

#### Atomic Commit (Main Thread)
- After the batch `JobHandle` completes:
  - For each slot: `Mesh.ApplyAndDisposeWritableMeshData(back, front)`.
  - Update chunk GameObject transforms to `origin`.
  - Update the grid's anchor GameObject Transform.position to the new anchor origin.
  - Clear staging; mark `volumeValid = true`.
- Presentation snaps in one frame; no partial updates.

#### Seams (Single Grid)
- Before meshing, ensure apron copy occurred for all adjacent pairs.
- During fairing, use seam-aware neighbor graph and soft band constraints.
- Optional post-mesh harmonization pass on seam faces to enforce equality.
- Optional tangential quantization (`voxelSize/256`) to remove drift.

#### Scheduling Notes
- Maintain a single batch `JobHandle` chaining all new fills → apron copies → meshing → stitching.
- Movement steps are always exactly 1 chunk along a single axis (no multi-chunk steps).
- Relocation completes before the player moves again; movement input is clamped/ignored until commit completes.

#### Relocation footprint: one-row slabs only
- On a single-axis step, only a single 1-slot-thick slab is repurposed on the leading face (the entering side), and a single 1-slot-thick slab is retired on the trailing face.
- In 3D terms, for a +X move:
  - Entering slab: all slots with `slot.x == dims.x - 1` (size `1 × dims.y × dims.z`).
  - Leaving slab: all slots with `slot.x == 0`.
- All interior slots (`1 .. dims.x-2`) retain their slot→world mapping; no relocation or remeshing beyond apron sync caused by the entering slab.
- The non-editable safety border can remain two slots thick for robustness, but only the outermost row is actually remapped per move; the inner safety row stays in place and non-editable.

#### In‑Flight Counters and Commit Event
- Track per-batch counters (atomic increments/decrements):
  - `generateInflight`, `apronInflight`, `meshInflight`, `stitchInflight`, `stampInflight` (interior-only stamps during batch).
  - A batch is Ready when all counters reach zero and the aggregate `JobHandle` is complete.
- On Ready, enable an event component on the grid entity:
  - `RollingGridCommitEvent` (enableable), contains `gridID` (or a reference to the grid entity).
- Presentation system observes the event and atomically applies all chunk updates for that grid in one frame, then disables the event.
- Chunks are associated by `NativeVoxelChunk.gridID`.

#### Stamps During Relocation (single‑chunk steps, no replays)
- We always move the grid by exactly 1 chunk along a single axis; only the two entering layers change mapping. Interior slots keep their mapping.
- Editing is prohibited on edge/entering chunks; stamps target interior chunks only.
- Stamps are accepted and applied as follows:
  - **No relocation batch active**: stamp → apron copy → mesh → apply front (current flow).
  - **Relocation batch active**: stamp → apron copy → mesh to staging only for the interior chunk; front remains unchanged until commit.
- There is no stamp replay for entering chunks; edits on entering/edge chunks are disallowed until after commit.
- Physics note: during a batch, prefer sampling front meshes/colliders; staged meshes are not yet presented.

#### APIs (Sketch)
- `BeginRelocation(int3 movedChunks)` → creates batch, schedules steps.
- `PollAndCommit()` → if ready, applies staging to front and updates transforms.
- Helpers:
  - `SlotOf(worldChunk)`, `WorldOf(slot)`, `ComputeChunkBounds(worldChunk)`.
  - `ScheduleGenerate(bounds, voxelSize, nvm.volume)`.
  - `ScheduleCopySharedOverlap(src, dst, axis)`.
  - `IncrementInflight(kind) / DecrementInflight(kind)`
  - `TrySignalReady(grid)` → enables `RollingGridCommitEvent` when all inflight counters are zero and fence is done.
  - `RollingGridCommitEvent { gridID, targetAnchorWorldChunk, targetOriginWorld }`
  - `GridAnchorAttachment { Transform attachTo }` – presentation anchor moved atomically at commit.
 
#### Authoring: VoxelMeshGrid (rolling)
- Add fields: `rolling: bool`, `slotDims:int3`, optional `anchor: Transform`.
- On Awake: if rolling is enabled, configure the grid entity (slotDims, voxelSize, initial anchor world chunk and origin).
- On Update (if rolling): compute `anchorWorldChunk` from `anchor.position` (or own transform). If a one‑chunk step is detected and no batch is active, send a move request (axis, sign) to the orchestrator; do not move transforms here.

#### Systems summary
- RollingGridOrchestratorSystem: detects single‑chunk moves, remaps only the entering one‑row slab, schedules background generation→apron→mesh (and optional seam harmonization), handles interior stamps to staging, tracks inflight counters, and raises `RollingGridCommitEvent` when ready.
- ManagedVoxelMeshingSystem: on commit event, atomically swaps staging→front, moves chunk GameObjects and the grid anchor GameObject, then disables the event. Avoid nested `Query<>` enumerations (gather arrays first, then iterate).

#### Testing
- Walk the grid ±X/±Y/±Z one chunk at a time; assert:
  - No visible crack after commit (ε ≤ 1e−5 on paired seam vertices).
  - No intermediate visual changes before commit.
  - Only new slots are regenerated; memory stable across moves.

#### Performance
- Work per move proportional to entering one‑row slab in moved direction.
- All operations reuse buffers; no heap churn.
- Background jobs amortize cost; main thread only swaps meshes and transforms.


