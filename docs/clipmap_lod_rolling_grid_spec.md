### Clipmap LOD Rolling Grid, Seam Stitching, and Background Relocation

#### Goals
- Seamless, sharp-detail voxel rendering across chunk seams and LOD transitions
- Sliding (toroidal) rolling grid with constant memory footprint
- Algorithm-agnostic post-mesh seam stitching (works with SurfaceNets/DC/MC)
- Background relocation per clipmap ring with atomic visual commit

#### Terminology
- **Chunk**: 32×32×32 voxel volume (`CHUNK_SIZE=32`) with 2-voxel apron (`CHUNK_OVERLAP=2`). World spacing: `EFFECTIVE_CHUNK_SIZE=30` voxels.
- **Ring (LOD k)**: Grid of chunks at voxel size `vk = v0 * 2^k`, excluding inner ring (k-1) bounds.
- **Rolling grid**: Fixed pool of chunk slots; world moves via anchor; slots remap toroidally.
- **Seam**: Shared face/edge/vertex between adjacent chunks, within same LOD or across LODs.

#### High-level Architecture
- Base generation and meshing unchanged; add two orthogonal subsystems:
  1) Clipmap ring manager with toroidal rolling grid per LOD
  2) SeamStitcher post-process (same-LOD and cross-LOD)
- Background relocation flow ensures zero visual popping: prepare → stitch → commit

#### Ringed Clipmap
- LOD rings: `grid0` (editable, v0), `gridk` (vk = v0·2^k), excluding previous ring bounds.
- For each ring k:
  - Maintain `NativeVoxelGrid` (`gridID`, `voxelSize=vk`, `bounds=ringBoundsWorld`).
  - Chunk lattice aligned to multiples of `EFFECTIVE_CHUNK_SIZE * vk`.
  - Keep a two-chunk thick inner border shell on all 6 faces (±X,±Y,±Z).

##### Movement Semantics
- Trigger move when anchor crosses one chunk along any axis.
- Only one ring moves at a time (clipmap property).
- Movement by +X: entering = outermost 2 layers at +X; leaving = outermost 2 at −X.

#### Toroidal Rolling Grid
- Fixed pool of `NativeVoxelMesh` slots sized to coverage dims per ring (e.g., 16×8×16).
- Map world chunk coords to slots:
  - `slot = Mod(worldChunk - anchorWorldChunk, dims)`
  - `origin = (float3)worldChunk * (EFFECTIVE_CHUNK_SIZE * vk)`
- On move: update `anchorWorldChunk`; slots persist; mark newly discovered slots.

##### Populate Newly Discovered Slots
- Compute world AABB from `worldChunk` and `vk`.
- Fill `nvm.volume`:
  - Option A: Downsample from LOD0 cached volume (2×2×2 label-aware).
  - Option B: Procedural generator at `vk`.
- Sync aprons with neighbors: `CopySharedOverlap` along resolved adjacency axis.

#### Meshing and Seam Stitching
- Run meshing as usual (SurfaceNets fairing or others); then SeamStitcher.
- Same-LOD seam fixes:
  - Symmetric neighbor graph in fairing around apron slabs
  - Seam constraints: tangential projection + optional soft band step attenuation
  - Optional harmonization pass: 1–2 sweeps enforcing equality on seam faces
- Cross-LOD seam fixes (k ↔ k+1):
  - Build mapping: each coarse seam cell ↔ 2× fine cells per perpendicular axis
  - Two strategies:
    1) Transition strips (index-only): watertight connectivity via stitching quads/triangles
    2) Harmonization: set coarse p = average(project(fine set)), nudge fine tangentially; label-aware
- Determinism:
  - Quantize seam vertex tangential components to `vk/256` after stitching
  - Project seam deltas to seam plane(s) to preserve sharp silhouettes
  - Label-aware attenuation (don’t blend across different materials)

#### Background Relocation with Atomic Commit
- Per-ring relocation batch with versioning:
  - States: Preparing → ReadyToCommit → Committing → Idle
  - Double-buffer meshes per slot: front (presented), back (staging `MeshData`)
- Background jobs (no visual change):
  1) Generate volumes for entering two-layer shell
  2) Copy aprons with interior/neighboring ring
  3) Mesh to back buffers
  4) SeamStitcher: same-LOD and cross-LOD
- Atomic commit (main thread) when all jobs complete:
  - Apply `Mesh.ApplyAndDisposeWritableMeshData(back, front)` for all slots in ring
  - Update transforms to new origins
  - Update `anchorWorldChunk` and ring mapping
- Cancel/replace if new move request arrives: version guard prevents stale commits

#### Scheduling Order (per move)
1. Mark entering two-layer shell, schedule generation → apron copy → meshing → stitcher
2. Wait for `ringHandle` (aggregate JobHandle)
3. Commit atomically on main thread
4. Retire leaving layers

#### Data Structures
- `NativeVoxelGrid`: unchanged (`gridID`, `voxelSize`, `bounds`)
- `NativeVoxelMesh`: reuse `volume`, `meshing`, `fairing` buffers per slot
- `SeamGraph`: adjacency over seam faces (same-LOD pairs, cross-LOD pairs)
- `RingRelocationBatch`:
  - `int ring; int version; int3 targetAnchor; JobHandle handle;`
  - Per-slot staging `MeshData` lists and slot->worldChunk mapping

#### Downsampling (LOD cache path)
- SDF: average 8 voxels or min-abs with average sign; preserve world scale
- Materials: majority vote (corner-sum) consistent with current per-vertex weights

#### Tests
- Same-LOD: seam vertex equality across 2×2×2 at each k (ε = 1e−5)
- Cross-LOD: C0 across k↔k+1 boundary using transition strips and harmonization
- Relocation: move +X/+Y/+Z; ensure no partial visual change until commit
- Material edges: label-aware stitching keeps sharp boundaries

#### Performance Notes
- Entering two layers per move minimize churn
- SeamStitcher operates on boundary subsets; tiny compared to full meshing
- Quantization is O(#seamVerts), negligible
- Transition strips avoid vertex mutations (fast path) when acceptable

#### Integration Steps
1) Implement toroidal mapping and batch relocation per ring
2) Add seam stitching passes (same-LOD first, cross-LOD transition strips)
3) Add background staging and atomic commit in presentation layer
4) Add tests and profiling

#### References
- SurfaceNets boundary preservation (JCGT) – label-aware smoothing and constraints: https://jcgt.org/published/0011/01/03/paper.pdf


