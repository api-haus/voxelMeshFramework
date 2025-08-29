### Voxel Materials via Vertex Color Encoding (Spec)

#### Purpose
- Encode per-voxel material IDs (u8) into mesh vertex colors to drive triplanar shading without splitting meshes, preserving a single draw call per chunk.

#### Non-goals and Constraints
- Materials are annotations only: they do not define geometry, topology, or SDF.
- Do not introduce "air" as a material. Do not reserve any id; treat all `u8` values as valid material annotations.
- Do not derive voxel SDF from material data. SDF authoring and editing remain independent.
- Keep a single mesh per chunk (no default submesh splits). Collider generation unchanged.

#### Data Model
- Storage (SoA) per chunk: `sdf: Box<[f32]>`, `mat: Box<[u8]>` with 1-voxel apron.
  - Constructors must initialize both arrays; provide `fill_default(sdf_value, material_id)`.
  - Authoring and editing write `mat` alongside `sdf` when solids are created/destroyed.
- Meshing: `fast-surface-nets` produces `SurfaceNetsBuffer { positions, normals, indices }`.
- Vertex materials: `vertex_materials: Vec<u8>` length equals `positions.len()`.

#### Vertex Material Selection (Surface Nets)
- For each generated vertex (one per sign-change cell):
  - Determine the cell min-corner `(cx, cy, cz)` by flooring the vertex position.
  - Clamp so that `(cx+1, cy+1, cz+1)` is within sample bounds.
  - Inspect the 8 cell-corner samples in `sdf` and `mat`.
  - Choose the corner with minimal `abs(sdf)`. On ties, select the majority material among tied corners; final fallback is the first in tie order.
  - Record the selected u8 into `vertex_materials` in the same order as `positions`.

Rationale:
- Surface Nets vertices lie within the cell, so flooring is a deterministic mapping to the cell that emitted the vertex.
- Tie-breaking prioritizes consistent labeling without requiring mesh splits.

#### Mesh Attribute Encoding
- Build a `Vec<[f32; 4]> colors` parallel to positions:
  - `r = mat_id as f32 / 255.0`, `g = 0.0`, `b = 0.0`, `a = 1.0`.
- Insert with `mesh.insert_attribute(Mesh::ATTRIBUTE_COLOR, colors)`.
- Continue outputting a single `Mesh` per chunk.

#### Shader Integration (rendering materials)
- Fragment shader reads vertex color through Bevy’s `VERTEX_COLORS` path.
- Decode material id: `let mat_id: u32 = u32(round(in.color.r * 255.0));`
- Phase 1 (debug): optional palette visualization controlled by a define (e.g., `DEBUG_MAT_VIS`), mapping `mat_id` to RGB for validation.
- Phase 2: use `mat_id` to select triplanar parameters for the rendering material:
  - MVP: small uniform arrays (e.g., color tints, tiling scale) indexed by `mat_id` modulo array length.
  - Future: 2D texture arrays or bindless textures for per-material albedo/normal/ORM.
- Preserve existing PBR lighting; only modify the base-color computation before lighting.

#### ECS/Systems
- No changes to `apply_mesh` API: it consumes a single `Mesh` and generates colliders as before.
- A future `RenderingMaterialLibrary` resource will map `u8` annotations to shader params/texture indices used by rendering materials.

#### Testing & Validation
- Unit tests (Rust) for vertex material selection:
  - Build small synthetic chunks (e.g., 3×3×3 samples) with known `sdf`/`mat` per cell.
  - Run the selection routine against synthetic vertex positions (or a small FSN output) and assert expected labels.
- Render sanity:
  - Seed scene with adjacent materials; verify per-vertex coloration differs while using a single mesh per chunk.
- Performance:
  - Ensure linear memory and no appreciable meshing slowdown; selection is O(vertices × 8).

#### Acceptance Criteria
- Each chunk produces a single `Mesh` containing `ATTRIBUTE_POSITION`, `ATTRIBUTE_NORMAL`, and `ATTRIBUTE_COLOR`.
- Vertex color encodes material id in the red channel as specified.
- Shader decodes `mat_id` and can visualize or apply per-material triplanar parameters.
- Empty/solid chunk skip remains intact; physics colliders unaffected.

#### Edge Cases
- Vertices near chunk borders: clamping ensures valid 8-corner reads.
- Mixed labels: selection follows minimal `abs(sdf)` and the tie rules; no special casing of ids.

#### Migration Notes
- Existing scenes compile unchanged; the shader gains optional debug path and material-indexing logic.
- Authoring seeds should be updated to write material annotations (`mat`) alongside `sdf` when solids are created.


