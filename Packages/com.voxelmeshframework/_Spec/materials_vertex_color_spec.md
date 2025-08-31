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
- Deprecated: discrete red-channel material-id encoding.
- RGBA encodes material weights; two modes:
  - BLENDED_RGBA_WEIGHTS: inverse-distance weights from the 8 corners.
  - BLENDED_CORNER_SUM: per-corner counts for the 8 corners, normalized.
- Continue outputting a single `Mesh` per chunk.

#### Shader Integration (rendering materials)
- Fragment shader reads vertex color through the vertex color attribute.
- Decode weights from RGBA and use them for triplanar/splat blending.
- Palette/array-texture lookup can be applied per channel as needed.
- Preserve existing PBR lighting; only modify the base-color computation before lighting.

#### ECS/Systems
- No changes to `apply_mesh` API: it consumes a single `Mesh` and generates colliders as before.
- A future `RenderingMaterialLibrary` resource will map `u8` annotations to shader params/texture indices used by rendering materials.

#### Testing & Validation
- Unit tests for vertex material selection:
  - Build small synthetic chunks (e.g., 3×3×3 samples) with known `sdf`/`mat` per cell.
  - Run the selection routine against synthetic vertex positions (or a small FSN output) and assert expected labels/weights.
- Render sanity:
  - Seed scene with adjacent materials; verify per-vertex coloration differs while using a single mesh per chunk.
- Performance:
  - Ensure linear memory and no appreciable meshing slowdown; selection is O(vertices × 8).

#### Acceptance Criteria
- Each chunk produces a single `Mesh` containing `ATTRIBUTE_POSITION`, `ATTRIBUTE_NORMAL`, and `ATTRIBUTE_COLOR`.
- Vertex color encodes material weights in RGBA as specified.
- Shader consumes weights and can visualize or apply per-material triplanar parameters.
- Empty/solid chunk skip remains intact; physics colliders unaffected.

#### Edge Cases
- Vertices near chunk borders: clamping ensures valid 8-corner reads.
- Mixed labels: selection follows minimal `abs(sdf)` and the tie rules; no special casing of ids.

#### Migration Notes
- Existing scenes compile unchanged; the shader gains weight-based blending logic.
- Authoring seeds should be updated to write material annotations (`mat`) alongside `sdf` when solids are created.


