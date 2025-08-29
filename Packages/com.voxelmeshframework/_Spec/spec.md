## Grid-based Voxel Meshing System (Unity C# + Burst/Jobs)

### Overview & Goals / Non-Goals
- **Goal**: CPU Surface Nets meshing for destructible, grid-based voxels across many independent volumes. Dense SoA voxel storage per chunk. Runtime edits, baking, PBR rendering, physics colliders.
- **Non-goals (v1)**: LOD, streaming, persistence beyond bake/load, GPU meshing, non-uniform scale support.
- **Platforms (production)**: Windows, Linux, macOS, iOS.
- **Editor access**: Scene editing is developer-only in a separate editor build. Shipping builds do not include editor UIs or authoring systems.

### Terminology & Coordinate Conventions
- **Volume space**: Local coordinates of a voxel volume. Chunks are axis-aligned here.
- **World space**: Unity `Transform` TRS; v1 assumes uniform scale per volume.
- **Voxel size**: Constant scalar; world units per lattice step.
- **Sample grid**: SDF/material defined at lattice nodes on integer grid in volume space.
- **Core vs apron**: Each chunk stores a core region plus +1 sample apron on all faces.
- **SDF semantics**: Negative=inside, positive=outside, iso=0.0.

## Data Model
- **Volume**
  - `voxel_size: f32`
  - `transform: Transform` (uniform scale asserted in v1)
  - `chunk_sample_dims: UVec3 = 32×32×32` (fixed sampling grid)
  - `chunk_core_dims: UVec3 = 30×30×30` (effective interior; apron inside samples)
  - `bounds: Option<Aabb>` (optional authoring/runtime culling)
  - `materials: [MaterialDesc; 256]` mapping `u8` → PBR + params
- **Chunk**
  - `core_dims: UVec3 = (Cx, Cy, Cz)` core cell counts
  - `sample_dims: UVec3 = (Cx+2, Cy+2, Cz+2)` lattice nodes including apron
  - `origin_cell: IVec3` core min index in lattice space
  - SoA storage: `sdf: Vec<f32>`, `mat: Vec<u8>`
  - Mesh state: per-material render submeshes; collider triangle soup
  - Dirty flags: `voxel_dirty`, `mesh_dirty`, `collider_dirty`
- **Indexing**: Linear index \(i = x + S_x (y + S_y z)\), `S = sample_dims`, `0<=x<Sx`, etc.
- **Bounds**:
  - Core cell AABB spans `[origin_cell, origin_cell + core_dims]` in lattice units.
  - Sample AABB spans `[origin_cell-1, origin_cell + core_dims+1]`.
  - Convert to world space by scaling with `voxel_size` and applying `transform`.

## Memory Layout & Budgets
- **SoA per chunk (samples)**
  - SDF: `4 * (Sx*Sy*Sz)` bytes
  - Mat: `1 * (Sx*Sy*Sz)` bytes

| Core cells (Cx×Cy×Cz) | Samples (Sx×Sy×Sz) | Samples count | SDF bytes | Mat bytes | Total/chunk |
| --------------------- | ------------------ | ------------- | --------- | --------- | ----------- |
| 30×30×30              | 32×32×32           | 32,768        | 128.0 KiB | 32.0 KiB  | 160.0 KiB   |

- **Surface Nets vertex bound**: \(V_{max} = C_x C_y C_z\); practical \(\approx 0.15 V_{max}\) for organic content.
- **Vertex format**: position(12) + normal(12) + uv(8) + tangent(16) = 48 B (tangents optional for PBR).
- **Peak RAM** (example): `volumes=N`, `chunks/vol=M`, `32³` core:
  - Voxels: `~192 KiB/chunk × N × M`
  - Meshes: `~48 B × 0.15 × C + indices` per vertex, transient work buffers ≤ 2× active meshing window.

## Apron (summary)
- Rationale: crack-free meshing (8-corner cells), stable SDF gradients, no cross-chunk reads during meshing.
- Sizing: sampling grid per chunk is fixed at `32×32×32`; effective core is `30×30×30` due to an internal 1-voxel apron.
- Ownership: interior indices per axis are `1..N`; apron indices `0` and `N+1` are read-only duplicates for meshing.
- Fill: copy neighbor interior edges (or a default SDF value at world bounds). When edits cross borders, mark both chunks dirty so aprons refresh before meshing.
- Overhead: `N=16 → ~42.38%`, `N=32 → ~19.95%`, `N=64 → ~9.67%` (slabs higher). See `docs/apron.md` for details.

## Authoring & Baking
- Visual authoring (editor mode)
  - Author scenes in-editor using ECS components for procedural shapes and mesh-to-SDF providers. We will integrate a Yoleck-like editor workflow for in-app editing, property widgets, and scene persistence. See Yoleck for inspiration and UX patterns [`bevy-yoleck`](https://github.com/idanarye/bevy-yoleck).
  - Editor mode controls entity creation/selection and component editing; game mode consumes only bakes. Editor mode ships as a separate build for developers.
- CSG ops:
  - `union(a,b)=min(a,b)`; `inter(a,b)=max(a,b)`; `sub(a,b)=max(a,-b)`.
  - Smooth variants (polynomial):
    - \(h = clamp(0.5 + 0.5\,(b-a)/k, 0, 1)\)
    - \(s = lerp(b, a, h) - k\,h\,(1-h)\)
- Material conflict rule (authoring): choose material from contributor with smallest \(|s|\); on ties use per-shape priority, else last-writer.
- Composition order: Flat list reduced in declared order; providers act like shapes.
- Voxelization sampling: Evaluate at lattice nodes; optional 2× supersample when \(|s| < 0.5\,voxel\_size\).
- Chunk culling: Evaluate primitive/provider only for chunks whose sample-space AABB intersects primitive AABB expanded by apron (+1 sample).
- Bake format (per-volume directory, `.vxb` per chunk):

| Field           | Type   | Value         |
| --------------- | ------ | ------------- |
| magic           | u32    | 'VXBF'        |
| version         | u32    | 1             |
| voxel_size      | f32    | world units   |
| chunk_core_dims | u16[3] | Cx, Cy, Cz    |
| apron           | u8     | 1             |
| sdf_type        | u8     | 0=f32         |
| material_bits   | u8     | 8             |
| reserved        | u8[13] | align to 32 B |

Body: `LZ4([sdf_f32 array], [mat_u8 array])`.
Optional: append CRC32 to detect corruption and aid hot-reload determinism checks.

- Load path: On startup, if bake exists → load; else author, then write bake.
- Dev hot-reload: File watcher reloads chunk files; swap-in storage and enqueue remesh; builds deterministic.
- Determinism: Fix thread pool size/order during authoring; identical inputs → identical outputs.
- Optional modifiers: Field/noise modifiers (e.g., FastNoise2 domain warp, masks) during authoring; keep deterministic by fixing seeds.

## ECS Schema (Authoring)
- **Shapes (world-space Bevy entities)**
  - `SdfSphere { radius: f32, material: u8, op: CsgOp, smooth_k: f32, priority: u8 }`
  - `SdfBox { half_extents: Vec3, material: u8, op, smooth_k, priority }`
  - `SdfCylinder { half_height: f32, radius: f32, material: u8, op, smooth_k, priority }`
  - `SdfCapsule { half_height: f32, radius: f32, material: u8, op, smooth_k, priority }`
  - `SdfCone { half_height: f32, radius_top: f32, radius_bottom: f32, material: u8, op, smooth_k, priority }`
  - All use `Transform`; evaluate in world space and transform to volume-local.
- **Blend ops**: `enum CsgOp { Union, Intersect, Subtract, SmoothUnion, SmoothIntersect, SmoothSubtract }` with `smooth_k`.
- **Mesh-to-SDF provider API**
  - `MeshSdfProvider { mesh: Handle<Mesh>, aabb_world: Aabb, resolution: f32, narrow_band: f32, sign: SignMode, default_material: u8, priority: u8 }`
  - `enum SignMode { WindingNumber, RayStabFloodFill }` (open meshes: fallback FloodFill)
  - Default narrow band: `±3 * voxel_size`; outside → large positive.

## Meshing (Surface Nets)
- **Sampling**: Use chunk `sdf[]` including apron; process cells whose min-corner lies inside core range (exclude terminal +X/+Y/+Z cell layer).
- **Vertex/index generation**: One vertex per zero-crossing cell; stitch faces to form indexed triangles; split by material into submeshes.
- **Fast paths**: If all `sdf > 0` or all `sdf < 0` in core+apron → skip mesh.
- **Normals**: Central differences with trilinear sampling at vertex position `p`:
  - \(\nabla s = normalize\big(( s(p+\delta x)-s(p-\delta x),\ s(p+\delta y)-s(p-\delta y),\ s(p+\delta z)-s(p-\delta z) )\big) / (2\,voxel\_size)\)
- **UV/tangent policy**: Default to shader triplanar (no UV attribute required). Planar UVs are optional; see buffer-to-mesh example if needed.
- **Material per-vertex (encoding)**: Choose corner with minimal \(|s|\) among 8 cell samples; assign its `mat` to the vertex and encode the material id in vertex color: `color.r = (mat_id as f32) / 255.0`, `color.g = color.b = 0.0`, `color.a = 1.0`. Render with a single mesh per chunk; the shader interprets `color.r` as a compact material index and applies triplanar shading/lookups accordingly. No per-material submesh split. Treat ids as annotations; no reserved values.
- **Transforms**: All sampling in volume-local. Gradients would use inverse-transpose for non-uniform scale (v1 uniform → no-op). Render/physics meshes use volume world transform.

## Editing & Remesh Pipeline
- **Edit ops** (`b`=brush SDF local, `s`=old SDF):
  - Place: `s' = min(s, b)`; where `sign(s')<0 && sign(s)>=0` set `mat = brush_mat`.
  - Destroy: `s' = max(s, -b)`; where `sign(s')>=0 && sign(s)<0` set `mat` as defined by the edit operation (e.g., brush annotation); SDF alone determines surface.
- **Shape-cast math**: `brush_local = inverse(volume.transform) * world_brush_transform`; evaluate brush SDF in local space.
- **Dirty-region → chunk mapping**: Brush AABB in volume space expanded by `apron_voxels * voxel_size` → convert to cell index range → overlap with chunk core ranges → mark chunks dirty and enqueue remesh.
- **Throttling/back-pressure**: Global remesh queue with budgets:
  - `max_chunks_per_frame` (default: 16)
  - `remesh_time_slice_ms` (default: ≤ 2.0 ms/frame)
  - Prioritize: visible first, then by camera distance, then last-edited first.
- **Determinism**: Stable priority ordering inside a frame; Surface Nets crate deterministic for fixed inputs.

## Chunk Boundaries & Overlaps
- **Authority**: Each chunk owns cells whose min-corner index is inside its core. Ignore terminal layers at +X/+Y/+Z borders.
- **Neighbor reads**: Only read local `sdf[]` including apron; never read neighbor arrays.
- **Crack avoidance**: +1 apron guarantees all 8 cell corners available locally; consistent cell ownership yields crack-free stitching.
 - **Interior ownership rule**: Interior indices per axis `1..N` are authoritative; aprons `0` and `N+1` are read-only.

## Bevy Integration
- **Components**
  - `VoxelVolume { voxel_size, chunk_core_dims, bounds, material_map }`
  - `VoxelChunk { origin_cell, core_dims, sample_dims }`
  - `VoxelStorage { sdf: Box<[f32]>, mat: Box<[u8]> }`
  - `ChunkMeshes { per_material: SmallVec<[Handle<Mesh>; N]> }`
  - `ChunkCollider { handle: Entity or ColliderRef }`
  - Tags: `NeedsRemesh`, `NeedsCollider`
- **Resources**
  - `VoxelRemeshQueue`, `RemeshBudget { max_chunks_per_frame, time_slice_ms }`
  - `MaterialLibrary { map: [Option<Handle<StandardMaterial>>; 256], params: [MaterialParams; 256] }`
- **Systems (ordered)**
  - Startup: authoring bake/load → build or load `VoxelStorage`
  - Update: runtime editing input → brush ops → mark dirty
  - PostUpdate: remesh scheduler (collect dirty, prioritize, dispatch jobs)
  - Async (Rayon): meshing workers producing `MeshData`
  - PostUpdate (main thread): apply meshes (create/update `Mesh` assets, assign materials)
  - PostUpdate: collider debounce and refresh (avian3d)
- **Asset lifetimes**: Mesh assets owned by chunk entities; destroyed on despawn; materials from library.

### Bevy ECS Plugin Integration (Volumes and Chunks are Entities)
- **Entity hierarchy**
  - Volume is a Bevy entity (`VoxelVolume` + `SpatialBundle`).
  - Each chunk is a Bevy entity (`VoxelChunk`, `VoxelStorage`, `ChunkMeshes`), parented to its volume via `Parent/Children`.
  - Chunk `Transform` is local to the volume; translation = `origin_cell * voxel_size` (in volume-local units).
- **System sets**
  - `VoxelSet::Authoring` (startup voxelization/bake/load)
  - `VoxelSet::Editing` (runtime edit intake → marks dirty)
  - `VoxelSet::Schedule` (remesh queue budgeting/prioritization)
  - `VoxelSet::ApplyMeshes` (drain async results → Mesh assets)
  - `VoxelSet::Physics` (collider debounce/update)
- **Events**
  - `VoxelEditEvent { volume: Entity, brush: Brush, xform: GlobalTransform, op: EditOp }`
  - `ChunkRemeshResult { chunk: Entity, buffer: SurfaceNetsBuffer }`

```rust
use bevy::prelude::*;
use fast_surface_nets::SurfaceNetsBuffer;

#[derive(SystemSet, Debug, Hash, PartialEq, Eq, Clone)]
pub enum VoxelSet { Authoring, Editing, Schedule, ApplyMeshes, Physics }

pub struct VoxelPlugin;

impl Plugin for VoxelPlugin {
    fn build(&self, app: &mut App) {
        app
            .insert_resource(RemeshBudget { max_chunks_per_frame: 16, time_slice_ms: 2.0 })
            .insert_resource(VoxelRemeshQueue::default())
            .add_event::<VoxelEditEvent>()
            .add_event::<ChunkRemeshResult>()
            .configure_sets(
                Update,
                (
                    VoxelSet::Editing,
                    VoxelSet::Schedule,
                    VoxelSet::ApplyMeshes,
                    VoxelSet::Physics,
                )
                    .chain(),
            )
            .add_systems(Startup, (load_or_author_bakes, spawn_volume_chunks).in_set(VoxelSet::Authoring))
            .add_systems(
                Update,
                (
                    apply_voxel_edits,       // EventReader<VoxelEditEvent> → write SDF, mark dirty
                    enqueue_dirty_chunks,    // push to VoxelRemeshQueue with priority
                )
                    .in_set(VoxelSet::Editing),
            )
            .add_systems(Update, (remesh_scheduler_frame,).in_set(VoxelSet::Schedule))
            .add_systems(
                Update,
                (
                    drain_remesh_results,    // EventReader<ChunkRemeshResult>
                    apply_meshes_to_chunks,  // spawn/update Mesh children per material
                )
                    .in_set(VoxelSet::ApplyMeshes),
            )
            .add_systems(PostUpdate, (collider_refresh_system,).in_set(VoxelSet::Physics));
    }
}

fn spawn_volume_chunks(mut commands: Commands, q_volume: Query<(Entity, &VoxelVolume, &GlobalTransform)>) {
    for (vol_e, volume, _xf) in &q_volume {
        for origin_cell in iter_chunk_origins(volume) {
            let chunk_e = commands
                .spawn((
                    Name::new("VoxelChunk"),
                    VoxelChunk { origin_cell, core_dims: volume.chunk_core_dims, sample_dims: volume.chunk_core_dims + UVec3::splat(2) },
                    VoxelStorage::new(volume.chunk_core_dims),
                    Transform::from_translation((origin_cell.as_vec3() * volume.voxel_size).into()),
                    GlobalTransform::IDENTITY,
                ))
                .set_parent(vol_e)
                .id();
            // Optionally tag for initial remesh
            commands.entity(chunk_e).insert(NeedsRemesh);
        }
    }
}

fn remesh_scheduler_frame(
    mut queue: ResMut<VoxelRemeshQueue>,
    budget: Res<RemeshBudget>,
    thread_pool: Res<bevy::tasks::ComputeTaskPool>,
    mut results: EventWriter<ChunkRemeshResult>,
    q_chunks: Query<(Entity, &VoxelChunk, &VoxelStorage, &Parent)>,
    q_volume: Query<&VoxelVolume>,
) {
    let start = bevy::utils::Instant::now();
    let mut used_ms = 0.0_f32;
    let mut count = 0;
    while count < budget.max_chunks_per_frame && used_ms < budget.time_slice_ms {
        if let Some(chunk_e) = queue.pop() {
            if let Ok((e, chunk, storage, parent)) = q_chunks.get(chunk_e) {
                let volume = q_volume.get(parent.get()).unwrap();
                // Clone minimal inputs for async job
                let sdf = storage.sdf.clone();
                let dims = chunk.core_dims;
                let job = thread_pool.spawn(async move {
                    if let Some(buf) = remesh_chunk_dispatch(&sdf, dims) { // see dispatch in previous section
                        Some((e, buf))
                    } else { None }
                });
                // In practice, collect via a channel; here we block for brevity
                if let Some((e, buf)) = futures_lite::future::block_on(job) {
                    results.send(ChunkRemeshResult { chunk: e, buffer: buf });
                }
                count += 1;
                used_ms = start.elapsed().as_secs_f32() * 1000.0;
            }
        } else { break; }
    }
}
```


## Physics Integration (avian3d)
- **Collider generation**: From combined render triangles of all per-material submeshes per chunk.
- **Update cadence**: Debounce to ≤ once per chunk per N frames (default: 1). Drop intermediate updates within window.
- **Broadphase hints**: Use chunk core AABB (world) as spatial hint; static if volume static.

## Concurrency & Scheduling
- **Threading**: Rayon or Bevy `ComputeTaskPool` for per-chunk parallelism (authoring voxelization and meshing). One job per chunk; no array sharing.
- **Budgets**: `max_chunks_per_frame = 16`, `remesh_time_slice_ms = 2.0` by default.
- **Time-slicing**: Measure CPU time; stop dequeuing when budget exhausted; resume next frame.

## API Surface (Rust)
- **Types**
  - `struct VoxelVolumeId(Uuid);`
  - `struct VoxelVolume { voxel_size: f32, chunk_core_dims: UVec3, bounds: Option<Aabb> }`
  - `enum CsgOp { Union, Intersect, Subtract, SmoothUnion{ k:f32 }, SmoothIntersect{ k:f32 }, SmoothSubtract{ k:f32 } }`
  - `struct Brush { shape: BrushShape, op: EditOp, material: u8 }`
  - `enum BrushShape { Sphere{ r:f32 }, Box{ half:Vec3 }, Cylinder{ r:f32, half_h:f32 }, Capsule{ r:f32, half_h:f32 }, Cone{ r_top:f32, r_bot:f32, half_h:f32 } }`
  - `enum EditOp { Place, Destroy }`
  - `struct MaterialParams { uv_scale:f32, sturdiness:f32, density:f32 }`
- **Functions**
  - `create_volume(commands, voxel_size, chunk_core_dims, bounds) -> Entity`
  - `author_volume(world, volume_entity) -> BakeResult`
  - `bake_volume_to_disk(volume_entity, dir: &Path)`
  - `load_volume_from_bake(commands, dir: &Path) -> Entity`
  - `shape_cast_place(volume_entity, brush: &Brush, world_xform: &GlobalTransform)`
  - `shape_cast_destroy(volume_entity, brush: &Brush, world_xform: &GlobalTransform)`
  - `queue_remesh_chunks(volume_entity, aabb_world: Aabb)`
  - `query_sdf(volume_entity, world_pos: Vec3) -> f32`
  - `despawn_volume(volume_entity)`
- **Trait**
  - `trait MeshToSdf { fn enqueue(&self, volume: Entity, params: MeshSdfProvider); }`

## Diagnostics & Testing
- **Counters/telemetry**: Chunks voxelized/meshed/culled per frame; mesh duration; queue lengths; collider updates.
- **Golden scenes**: Deterministic authoring scenes; baked outputs hashed; compare mesh vertex/index hashes.
- **Crack tests**: Sweep plane/sphere across chunk boundaries; assert identical vertices on shared edges.
- **Regression suite**: Place/destroy stress, mixed materials, mesh-to-SDF signs, apron correctness, determinism across platforms.

## Risks & Future Work
- **Non-uniform scale**: Requires world-space vertex solve and gradient inverse-transpose; defer to v2.
- **Compression/streaming**: zstd for size (future); streaming by visibility.
- **LOD**: Coarser chunks or dual-grid; out of scope.
- **GPU meshing**: Future compute path for heavy edit rates.

## Tunables / Defaults
- **MAX_MATERIALS**: 256 (`u8` index, simple)
- **APRON**: 1 sample (crack-free minimal)
- **Chunk size**: fixed sampling `32×32×32` (effective interior `30×30×30`).
- **Smooth `k` default**: `k = 1.5 * voxel_size` (soft but crisp)
- **Mesh-to-SDF narrow band**: `±3 * voxel_size` (speed)
- **Bake compression**: LZ4 (dev speed; optional zstd level 3 for shipping)
- **Remesh budget**: `≤ 2.0 ms/frame`, `max_chunks_per_frame=16`

## Acceptance Criteria
- **Crack-free** boundaries with +1 apron; deterministic meshes per input.
- **Meshing CPU** ≤ 2.0 ms/frame typical edit rates at 60/120 FPS; bursts queue without ≥ 4 ms hitch.
- **Edit latency** (shape-cast → visible mesh) ≤ 33 ms typical, ≤ 100 ms worst-case.
- **Collider freshness** (mesh → collider) ≤ 66 ms typical with 1-frame debounce.
- **Memory ceilings** respected: default cap 256 active meshed chunks; exceeding → throttle remesh and drop lowest-priority items.

## Pseudocode

### Authoring voxelization → `sdf[]/mat[]` (apron-correct, per-chunk culling)
```rust
fn voxelize_volume(volume: &Volume, shapes: &[AuthoringShape], mesh_providers: &[MeshSdfProvider]) {
    volume.chunks.par_iter_mut().for_each(|chunk| {
        let sample_aabb_world = chunk.sample_aabb_world(volume);

        let active_shapes = shapes.iter()
            .filter(|s| s.world_aabb_expanded().intersects(sample_aabb_world));
        let active_meshes = mesh_providers.iter()
            .filter(|m| m.aabb_world_expanded().intersects(sample_aabb_world));

        // Initialize storage
        chunk.sdf.fill(F32_LARGE_POS);
        chunk.mat.fill(0);

        // Reduce shapes
        for s in active_shapes {
            let to_local = volume.transform.compute_matrix().inverse();
            let shape_local = to_local * s.transform.compute_matrix();
            for (idx, pos_local) in chunk.iter_sample_positions_local(volume.voxel_size) {
                let d = s.eval_sdf_local(pos_local, shape_local);
                let s_old = chunk.sdf[idx];
                let m_old = chunk.mat[idx];
                let (s_new, picked_mat) = csg_combine(s_old, d, s.op, s.smooth_k, m_old, s.material, s.priority);
                chunk.sdf[idx] = s_new;
                chunk.mat[idx] = picked_mat;
            }
        }

        // Mesh-SDF providers (narrow band)
        for m in active_meshes {
            let to_local = volume.transform.compute_matrix().inverse();
            let provider_local_aabb = m.aabb_world * to_local;
            for (idx, pos_local) in chunk.iter_sample_positions_local(volume.voxel_size) {
                if !provider_local_aabb.contains(pos_local) { continue; }
                let d = sample_mesh_sdf(m, volume.to_world(pos_local)); // world sampling if provider needs
                if d.abs() > m.narrow_band { continue; }
                let s_old = chunk.sdf[idx];
                let m_old = chunk.mat[idx];
                let (s_new, picked_mat) = csg_combine(s_old, d, CsgOp::Union, 0.0, m_old, m.default_material, m.priority);
                chunk.sdf[idx] = s_new;
                chunk.mat[idx] = picked_mat;
            }
        }
    });
}
```

### CSG combine with material rule
```rust
fn csg_combine(s_old: f32, d: f32, op: CsgOp, k: f32, mat_old: u8, mat_new: u8, pri_new: u8) -> (f32, u8) {
    let s = match op {
        CsgOp::Union => s_old.min(d),
        CsgOp::Intersect => s_old.max(d),
        CsgOp::Subtract => s_old.max(-d),
        CsgOp::SmoothUnion { .. } => smooth_union(s_old, d, k),
        CsgOp::SmoothIntersect { .. } => smooth_intersect(s_old, d, k),
        CsgOp::SmoothSubtract { .. } => smooth_subtract(s_old, d, k),
    };
    // Material: choose contributor nearest to surface
    let mut m = mat_old;
    let abs_old = s_old.abs();
    let abs_new = d.abs();
    if abs_new < abs_old {
        m = mat_new;
    } else if (abs_new - abs_old).abs() < f32::EPSILON {
        // tie-break by priority, else last-writer (new)
        if pri_new >= 128 { // example: higher bit wins; adapt to your scheme
            m = mat_new;
        }
    }
    (s, m)
}
```

### Edit application (place/destroy) and material writes
```rust
fn apply_brush(volume: &mut Volume, brush: &Brush, world_xform: &GlobalTransform) {
    let brush_local = volume.transform.affine().inverse() * world_xform.affine();
    let brush_aabb_local = brush.aabb_local().transform(brush_local).expand(volume.voxel_size);
    for chunk in volume.chunks_overlapping_aabb(brush_aabb_local) {
        let mut changed = false;
        chunk.edit_samples(|pos_local, idx| {
            let b = brush.eval_sdf_local(pos_local, brush_local);
            let s = chunk.sdf[idx];
            match brush.op {
                EditOp::Place => {
                    let s_new = s.min(b);
                    if s_new < 0.0 && s >= 0.0 { chunk.mat[idx] = brush.material; changed = true; }
                    if s_new != s { chunk.sdf[idx] = s_new; changed = true; }
                }
                EditOp::Destroy => {
                    let s_new = s.max(-b);
                    if s_new >= 0.0 && s < 0.0 { /* edit rule may clear or set a material id; SDF decides surface */ changed = true; }
                    if s_new != s { chunk.sdf[idx] = s_new; changed = true; }
                }
            }
        });
        if changed { mark_dirty_and_enqueue_remesh(chunk.id); }
    }
}
```

### Dirty-region → chunk mapping & remesh queueing
```rust
fn mark_dirty_and_enqueue_remesh(chunk_id: ChunkId) {
    if !DIRTY_SET.insert(chunk_id) { return; } // dedupe
    REMESH_QUEUE.push(priority_for(chunk_id), chunk_id);
}

fn remesh_scheduler_frame() {
    let start = Instant::now();
    let mut used_ms = 0.0_f32;
    let mut count = 0;
    while count < BUDGET.max_chunks_per_frame && used_ms < BUDGET.remesh_time_slice_ms {
        if let Some((_key, chunk_id)) = REMESH_QUEUE.pop() {
            remesh_chunk_async(chunk_id); // Rayon
            count += 1;
            used_ms = start.elapsed().as_secs_f32() * 1000.0;
        } else { break; }
    }
}
```

### Surface Nets invocation (fast_surface_nets style, ConstShape dispatch)
```rust
use fast_surface_nets::{surface_nets, SurfaceNetsBuffer};
use fast_surface_nets::ndshape::ConstShape3u32;

// Example: fixed 16³ core with +1 apron → 18³ samples
const UNPADDED_CHUNK_SIDE: u32 = 16;
type PaddedShape16 = ConstShape3u32<{ UNPADDED_CHUNK_SIDE + 2 }, { UNPADDED_CHUNK_SIDE + 2 }, { UNPADDED_CHUNK_SIDE + 2 }>;

fn remesh_chunk_fixed_16(chunk: &VoxelChunk, volume: &Volume) -> Option<SurfaceNetsBuffer> {
    // Fast-path: skip when entirely positive or negative (includes apron)
    if chunk.sdf.iter().all(|&s| s > 0.0) || chunk.sdf.iter().all(|&s| s < 0.0) { return None; }

    // The mesher expects a contiguous sample field matching the shape size
    debug_assert_eq!(chunk.sdf.len(), PaddedShape16::SIZE as usize);

    let mut buffer = SurfaceNetsBuffer::default();
    // Cells region = core cells only → [0..UNPADDED+1). Using +1 because cells = samples-1
    surface_nets(
        &chunk.sdf,
        &PaddedShape16 {},
        [0; 3],
        [UNPADDED_CHUNK_SIDE + 1; 3],
        &mut buffer,
    );
    if buffer.positions.is_empty() { None } else { Some(buffer) }
}

// Dispatch for allowed sizes {16³, 32³, 64×64×8}
fn remesh_chunk_dispatch(chunk: &VoxelChunk, volume: &Volume) -> Option<SurfaceNetsBuffer> {
    match (volume.chunk_core_dims.x, volume.chunk_core_dims.y, volume.chunk_core_dims.z) {
        (16, 16, 16) => remesh_chunk_fixed_16(chunk, volume),
        (32, 32, 32) => remesh_chunk_fixed::<32, 32, 32>(chunk, volume),
        (64, 64, 8) => remesh_chunk_fixed::<64, 64, 8>(chunk, volume),
        _ => unimplemented!("unsupported chunk core dims"),
    }
}

// Generic fixed-shape implementation (compile-time dims)
fn remesh_chunk_fixed<const CX: u32, const CY: u32, const CZ: u32>(
    chunk: &VoxelChunk,
    volume: &Volume,
) -> Option<SurfaceNetsBuffer> {
    type Padded<const X: u32, const Y: u32, const Z: u32> = ConstShape3u32<{ X + 2 }, { Y + 2 }, { Z + 2 }>;

    if chunk.sdf.iter().all(|&s| s > 0.0) || chunk.sdf.iter().all(|&s| s < 0.0) { return None; }
    debug_assert_eq!(chunk.sdf.len(), Padded::<CX, CY, CZ>::SIZE as usize);

    let mut buffer = SurfaceNetsBuffer::default();
    surface_nets(
        &chunk.sdf,
        &Padded::<CX, CY, CZ> {},
        [0; 3],
        [CX + 1, CY + 1, CZ + 1],
        &mut buffer,
    );
    if buffer.positions.is_empty() { None } else { Some(buffer) }
}
```

### Buffer → Bevy `Mesh` (single mesh, material id in vertex color)
```rust
use bevy::prelude::*;

fn buffer_to_mesh_single(
    chunk: &VoxelChunk,
    volume: &Volume,
    buffer: &SurfaceNetsBuffer,
) -> Mesh {
    // For each vertex, pick material from nearest cell corners (minimal |s| among 8 corners)
    let mut positions_out: Vec<[f32; 3]> = Vec::with_capacity(buffer.positions.len());
    let mut normals_out: Vec<[f32; 3]> = Vec::with_capacity(buffer.normals.len());
    let mut colors_out: Vec<[f32; 4]> = Vec::with_capacity(buffer.positions.len());
    let mut indices_out: Vec<u32> = buffer.indices.clone();

    // Positions/normals come from SurfaceNetsBuffer (computed via SDF gradients internally)
    // UVs: compute simple dominant-axis planar UVs
    let positions = &buffer.positions;
    let normals = &buffer.normals;

    // Precompute per-vertex material by sampling chunk.sdf/mat in the cell that emitted the vertex
    let mut vertex_materials = vec![0u8; positions.len()];
    for (vi, (&[x, y, z], _n)) in positions.iter().zip(normals.iter()).enumerate() {
        let p_local = Vec3::new(x, y, z);
        // Convert to lattice cell coordinates; clamp to core cell range
        let cell = world_pos_to_core_cell(chunk, volume, p_local);
        let mat_id = pick_material_from_cell_corners(chunk, cell);
        vertex_materials[vi] = mat_id;
    }

    for (vi, p) in positions.iter().enumerate() {
        positions_out.push(*p);
        normals_out.push(normals[vi]);
        let r = (vertex_materials[vi] as f32) / 255.0;
        colors_out.push([r, 0.0, 0.0, 1.0]);
    }

    let mut mesh = Mesh::new(bevy::render::render_resource::PrimitiveTopology::TriangleList);
    mesh.insert_attribute(Mesh::ATTRIBUTE_POSITION, positions_out);
    mesh.insert_attribute(Mesh::ATTRIBUTE_NORMAL, normals_out);
    mesh.insert_attribute(Mesh::ATTRIBUTE_COLOR, colors_out);
    mesh.insert_indices(bevy::render::mesh::Indices::U32(indices_out));
    mesh
}
```

### Rayon meshing driver (ilattice + par_bridge)
```rust
use ilattice::prelude::*;
use rayon::prelude::*;

fn remesh_visible_chunks(volume: &Volume, visible_chunk_extent: Extent<IVec3>) {
    let core = IVec3::new(
        volume.chunk_core_dims.x as i32,
        volume.chunk_core_dims.y as i32,
        volume.chunk_core_dims.z as i32,
    );
    visible_chunk_extent
        .iter3()
        .par_bridge()
        .for_each(|chunk_coord| {
            let chunk_min = chunk_coord * core; // lattice coords
            let padded_extent = Extent::from_min_and_shape(chunk_min, core).padded(1);
            // Fill or update chunk.sdf/mat for this extent (editing/authoring path)
            // Then call remesh_chunk_dispatch(...) and send result back to main thread for Mesh creation
        });
}
```

### Voxel fill loop using ilattice + `ConstShape3u32`
```rust
use fast_surface_nets::ndshape::ConstShape3u32;
use ilattice::prelude::*;

// Example for 16³ core (+2 padded samples)
const SIDE: u32 = 16;
type Padded = ConstShape3u32<{ SIDE + 2 }, { SIDE + 2 }, { SIDE + 2 }>;

fn fill_chunk_sdf_with_shapes(
    padded_extent: Extent<IVec3>,
    sdf_out: &mut [f32],
    default_sdf: f32,
    shapes: &[Sphere],
) {
    sdf_out.fill(default_sdf);
    padded_extent.iter3().for_each(|pwo| {
        let p = pwo - padded_extent.minimum; // padded-chunk local
        let idx = Padded::linearize([p.x as u32, p.y as u32, p.z as u32]) as usize;
        let pwof = pwo.as_vec3a();
        for s in shapes {
            if s.extent.contains(pwo) {
                let d = (s.origin - pwof).length() - s.radius;
                // union
                unsafe {
                    let v = sdf_out.get_unchecked_mut(idx);
                    *v = v.min(d);
                }
            }
        }
    });
}
```

### Bevy ECS integration and collider refresh (avian3d)
```rust
fn apply_meshes_to_chunk(commands: &mut Commands, chunk_e: Entity, meshes: Vec<MeshPerMaterial>) {
    // Update per-material child entities for rendering
    commands.entity(chunk_e).insert(NeedsCollider);
}

fn collider_refresh_system(mut q: Query<(Entity, &mut NeedsCollider, &GlobalTransform, &ChunkMeshes)>) {
    for (e, _, xf, meshes) in q.iter_mut() {
        if !debounce_ready(e) { continue; }
        let tri_iter = meshes.iter().flat_map(|m| m.triangles_world());
        let collider = avian3d::collider::from_triangles(tri_iter);
        // attach/update collider, clear NeedsCollider
    }
}
```

### Bake I/O & hot-reload (dev)
```rust
fn write_vxb(path: &Path, chunk: &VoxelChunk, volume: &Volume) {
    let header = Header::new(volume.voxel_size, chunk.core_dims, 1);
    let mut buf = Vec::with_capacity(chunk.sdf.len()*4 + chunk.mat.len());
    buf.extend_from_slice(bytemuck::cast_slice(&chunk.sdf));
    buf.extend_from_slice(&chunk.mat);
    let compressed = lz4_flex::compress_prepend_size(&buf);
    write_all(path, header.as_bytes());
    write_all(path, &compressed);
}

fn read_vxb(path: &Path) -> (Vec<f32>, Vec<u8>, Header) {
    let header = Header::read(path).unwrap();
    let data = read_all(path, header.size_bytes..).unwrap();
    let decompressed = lz4_flex::decompress_size_prepended(&data).unwrap();
    let (sdf_bytes, mat_bytes) = decompressed.split_at(header.sample_count()*4);
    (cast_slice_to_vec_f32(sdf_bytes), mat_bytes.to_vec(), header)
}

fn hot_reload_watcher() {
    for evt in fs_watch_bake_dir() {
        if evt.is_modify() {
            let (sdf, mat, header) = read_vxb(&evt.path);
            let chunk = find_chunk_from_path(&evt.path);
            chunk.replace_storage(sdf, mat, header);
            mark_dirty_and_enqueue_remesh(chunk.id);
        }
    }
}
```

## Open Choices (recommended)
- **Chunk size**: fixed sampling `32×32×32`; no alternative sizes in v1.
- **Remesh prioritization**: on-screen chunks first, then by squared distance to active camera; tie-break by last-edit timestamp.
- **Smooth blend `k`**: default `1.5 * voxel_size`; per-shape override.
- **Mesh-to-SDF sign**: default `WindingNumber`; fallback `FloodFill` for open meshes.
- **Bake compression**: `LZ4` default (dev speed); optional `zstd` level 3 for shipping.
- **Determinism**: Fix Rayon pool size; stable chunk iteration; disable FMA if cross-CPU diffs appear.

## Performance Targets & Budgets (v1)
- Remesh CPU: ≤ 2.0 ms/frame typical at 60/120 FPS (desktop); cap `max_chunks_per_frame` to 16 (desktop) or 8 (mobile).
- Edit latency (brush → visible): ≤ 33 ms typical, ≤ 100 ms worst-case bursts.
- Collider freshness (mesh → collider): ≤ 66 ms typical with 1-frame debounce.
- Determinism: identical inputs produce identical outputs on same OS/CPU; fix thread pool size and authoring seeds.

## Dependencies (Crates)
- bevy — engine/ECS/rendering; asset management.
- rayon — parallel voxelization/remeshing.
- fast-surface-nets — CPU Surface Nets extraction for regular grids.
- mesh_to_sdf — triangle mesh → SDF for authoring providers.
- fastnoise2 — optional field/noise modifiers during authoring.

```toml
[dependencies]
bevy = "0.16"
rayon = "1"
fast-surface-nets = "0.2"
mesh_to_sdf = "0.4"
fastnoise2 = "0.3"
```


