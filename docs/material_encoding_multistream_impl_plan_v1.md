### Multi-stream Material Encoding for Voxel Meshing — Implementation Plan (v1)

#### Summary
- Decouple material colors/weights from the third-party `Vertex` layout and move material computation into dedicated jobs.
- Support flexible encodings: 4/8/12/16-channel weight blocks and palette-based colors, while keeping a single mesh per chunk.
- Adopt multi-stream vertex buffers and a reusable per-4-channel job to generate packed `UNorm8×4` blocks (`Color32`).

#### Goals
- Compute per-vertex material labels/weights in a separate Burst job, independent of surface extraction.
- Provide a scalable encoding pipeline: 4, 8, 12, 16 weights, or palette color.
- Keep the mesh a single submesh; colliders and FSN geometry generation remain unchanged.
- Minimize coupling to 3rd-party Surface Nets structures.

#### Non-goals
- Changing FSN algorithm or its data layout beyond reading positions/normals/tangents.
- Introducing submesh splits or multiple draw calls.
- Material system/palette authoring UI (future work).

---

### Architecture Overview

1) Surface extraction (existing)
- FSN generates geometry (positions, normals, tangents) and indices.
- We avoid depending on FSN’s internal corner sampling; materials are computed in a separate pass.

2) Vertex-to-cell mapping (existing/extended)
- Reuse or compute mapping from each vertex to its originating cell via floor-and-clamp from vertex positions.
- Stored in `MeshingAttributeBuffers` (renamed from `FairingBuffers`).

3) Material computation (new)
- A reusable `ComputeMaterialBlockJob` runs once per 4-channel block to produce `NativeArray<Color32>` buffers.
- Two strategies:
  - Selection: pick the material by minimal `abs(sdf)` among the 8 cell corners (spec tie-break rules).
  - Blended weights: derive per-corner/label weights (e.g., inverse-distance or corner counts) and normalize into 4-channel blocks.
- Palette mode: map selected material ID to an RGBA color in a palette and write it as the block.

4) Mesh upload (extended)
- `UploadMeshJob` builds a dynamic vertex format with multiple streams:
  - Stream 0: Position (Float3), Normal (Float3), Tangent (Float4).
  - Additional streams: one `UNorm8×4` attribute per computed material block.
- Default/back-compat: for 4 channels, write to `VertexAttribute.Color` in stream 0. For >4, add extra attributes as `TexCoord0..2` with `UNorm8×4`, each potentially in its own stream.

5) Shader consumption (extended)
- Shaders read `COLOR` and optionally `TEXCOORD0..2` as additional packed `UNorm8×4` blocks.
- Decode to weights or palette color according to the selected encoding mode.

---

### Encoding Modes

```csharp
public enum MaterialEncodingMode
{
	None,
	WeightsRGBA4,
	WeightsRGBA8,
	WeightsRGBA12,
	WeightsRGBA16,
	PaletteColor32,
}
```

- Blocks required per mode:
  - WeightsRGBA4 → 1 block
  - WeightsRGBA8 → 2 blocks
  - WeightsRGBA12 → 3 blocks
  - WeightsRGBA16 → 4 blocks
  - PaletteColor32 → 1 block

Stream/attribute layout:
- 4 channels (back-compat):
  - Stream 0: Position, Normal, Tangent, Color (UNorm8×4) → reads in shaders as `COLOR`
- 8 channels:
  - Stream 0: Position, Normal, Tangent, Color (UNorm8×4)
  - Stream 1: TexCoord0 (UNorm8×4) → reads as `TEXCOORD0`
- 12 channels:
  - + Stream 2: TexCoord1 (UNorm8×4) → reads as `TEXCOORD1`
- 16 channels:
  - + Stream 3: TexCoord2 (UNorm8×4) → reads as `TEXCOORD2`
- PaletteColor32:
  - Use Color in stream 0 for the palette color (no extra streams)

Notes:
- Using UNorm8 minimizes bandwidth and aligns with `Color32` packing.
- Additional blocks can be assigned to separate streams to reduce interleaving cost and simplify uploads.

---

### Data Structures and Buffers

- `MeshingAttributeBuffers` (renamed from `FairingBuffers`):
  - `NativeList<int3> cellCoords` and/or `NativeList<int> cellLinearIndex`
  - `NativeList<byte> materialIds` (if selection mode needs to persist labels)
  - `NativeList<float4> materialWeights` (optional scratch for intermediate weights)
  - `NativeArray<int> cellToVertex` (dense, CHUNK_SIZE^3; used for mapping when needed)
  - `NativeList<int2> neighborIndexRanges`, `NativeList<int> neighborIndices` (for fairing; unaffected)
  - `NativeList<float3> positionsA/B`, `NativeList<float3> normals` (for fairing)
  - New: `NativeList<Color32> materialBlock0..3` (or an indexed list/array of blocks) sized to vertex count

Allocation strategy:
- After FSN completes and vertex count V is known, allocate `V` entries for each required block.
- Prefer `NativeArray<Color32>` for blocks if the count is fixed per upload; `NativeList<Color32>` if dynamic.

---

### Jobs

1) ComputeMaterialBlockJob (new)
- Interface: `IJobParallelFor`
- Inputs:
  - `NativeArray<float>` sdf (with apron)
  - `NativeArray<byte>` mat (with apron)
  - `NativeArray<int>` cellLinearIndex (length = vertex count)
  - Dimensions: `dimX, dimY, dimZ` (sample grid including apron)
  - `blockIndex` (0..3)
  - `MaterialEncodingMode mode`
  - Options: `inverseDistance`, `cornerCount`, `fastMath`, etc.
- Output:
  - `NativeArray<Color32> blockOut` (length = vertex count)
- Behavior:
  - Selection path: select corner/material per spec (min `abs(sdf)`, ties → majority material → first by order). Map to one-hot or palette color depending on `mode`.
  - Blended path: compute 8-corner contributions → select 4 designated channels for this `blockIndex`, normalize to UNorm8×4 and pack into `Color32`.

2) Encode/Upload (existing UploadMeshJob, extended)
- Assemble `VertexAttributeDescriptor[]` with explicit `stream` indices and `UNorm8×4` descriptors for blocks.
- Copy block data into `MeshData` via `GetVertexData<Color32>(stream)`.

Batching and scheduling:
- Use `IJobParallelFor` with a batch size of 64–128 for good occupancy.
- Schedule 1..4 instances of `ComputeMaterialBlockJob` based on mode; they can run in parallel with separate outputs.

---

### Mesh Upload Details

- Descriptor construction (conceptual):
```csharp
// Stream 0
descs.Add(new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0));
descs.Add(new VertexAttributeDescriptor(VertexAttribute.Normal,   VertexAttributeFormat.Float32, 3, 0));
descs.Add(new VertexAttributeDescriptor(VertexAttribute.Tangent,  VertexAttributeFormat.Float32, 4, 0));

if (mode == WeightsRGBA4 || mode == PaletteColor32)
{
	descs.Add(new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, 0));
}

// Additional streams for extra blocks
int stream = 1;
for (int i = 1; i < blocks; i++)
{
	var attr = (i - 1) switch
	{
		0 => VertexAttribute.TexCoord0,
		1 => VertexAttribute.TexCoord1,
		_ => VertexAttribute.TexCoord2,
	};
	descs.Add(new VertexAttributeDescriptor(attr, VertexAttributeFormat.UNorm8, 4, stream++));
}
```

- Data copy:
```csharp
md.SetVertexBufferParams(vertexCount, descsArray);
md.GetVertexData<Vertex>(0).CopyFrom(positionsNormalsTangents);
md.GetVertexData<Color32>(colorStreamIndex).CopyFrom(block0);
// ... repeat for additional block streams
```

---

### Configuration and API Surface

- Add a central configuration struct/resource consumed by the meshing system:
```csharp
public struct MaterialEncodingOptions
{
	public MaterialEncodingMode mode;
	public int batchSize; // e.g., 64 or 128
	public bool fastMath;
}
```
- The orchestrator determines `blocks = mode switch { 4→1, 8→2, 12→3, 16→4, palette→1 }` and allocates/schedules accordingly.

---

### Compatibility and Migration

- Default mode: `WeightsRGBA4` writing to `COLOR` in stream 0 to preserve existing shaders.
- Multi-stream modes introduce additional `TEXCOORD` attributes; update shaders to read them when enabled.
- FSN `Vertex` can remain unchanged; we simply stop depending on its `.color` when multi-stream is active.

---

### Testing

- Unit tests (playmode or editmode):
  - Vertex material selection (min-abs-sdf, tie-breaking) on synthetic 3×3×3 chunks.
  - Blended weights correctness: normalization and block partitioning.
  - Palette color mapping for selected labels.
- Runtime sanity:
  - Scene with adjacent labeled regions: verify per-vertex colors/weights for 4/8/12/16 modes.
  - Ensure single submesh and stable indices.
- Performance:
  - Profiler markers for each job and upload path; compare single-block vs multiple-block modes.

---

### Performance Expectations

- Material jobs are O(V) and memory-bound due to 8-corner reads per vertex; cost scales nearly linearly with the number of blocks.
- With Burst and UNorm8 packing, a single block typically adds a small fraction of FSN time; 2–4 blocks scale proportionally.
- Parallel scheduling across blocks is viable if memory bandwidth permits; otherwise schedule sequentially to avoid contention.

---

### Risks and Mitigations

- Vertex attribute compatibility across Unity versions: ensure `VertexAttributeDescriptor.stream` is available; fall back to single-stream packing if needed.
- Shader divergence: guard reads by keywords per mode; supply debug visualization.
- Memory overhead: additional `Color32[V]` arrays (up to 4×) — acceptable for typical chunk vertex counts.

---

### Task Breakdown (initial)

1) Add `MaterialEncodingMode` and `MaterialEncodingOptions` (config).
2) Rename `FairingBuffers` → `MeshingAttributeBuffers`; add material block arrays.
3) Implement `ComputeMaterialBlockJob` and tests.
4) Extend orchestration to allocate/schedule 1..4 block jobs per mode.
5) Update `UploadMeshJob` to assemble descriptors and upload extra streams.
6) Update shaders to read additional `UNorm8×4` blocks as `TEXCOORD0..2`.
7) Benchmarks and tuning (batch size, parallel blocks, packing).

---

### Open Questions

- Should the first 4-channel block always occupy `COLOR` in stream 0 for simplicity, or unify on `TEXCOORD` for all blocks?
- Do we want an additional mode for `WeightsRGBA4 + PaletteColor32` simultaneously? (Two blocks.)
- Where should the palette live (global resource vs. per-world)?


