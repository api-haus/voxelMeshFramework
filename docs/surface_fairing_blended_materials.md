## Surface Fairing for Naive Surface Nets with Blended Materials (Corner‑Sum)

### Summary
This document describes a post‑process fairing pipeline for Naive Surface Nets when materials are represented by a smooth corner‑sum interpolation over up to four materials, encoded in vertex color (RGBA). The goal is to improve geometric smoothness while preserving visually sharp transitions where material mixtures change rapidly.

Unlike the multi‑label (discrete) setting, we do not carry a single material ID per vertex. Instead, each vertex stores a vector of material weights `w = (w0, w1, w2, w3)` with `w ≥ 0` and typically `Σw ≈ 1`. Sharp transitions are detected using differences in material weights across neighboring vertices rather than exact label changes.

Reference inspiration: SurfaceNets for Multi‑Label Segmentations with Preservation of Sharp Boundaries ([JCGT 2022 paper](https://jcgt.org/published/0011/01/03/paper.pdf)). We adapt the boundary‑preserving idea from discrete labels to blended weights.

---

### Inputs
- **Mesh topology**: One vertex per active cell and triangle indices from Naive Surface Nets (unchanged).
- **Vertex attributes**:
  - `position`: current vertex position.
  - `color`: RGBA encodes up to 4 material weights `(w0..w3)`; assumed normalized to `[0,1]` and approximately summing to 1.
- **Voxel/cell data**: Original cell coordinate of each vertex and voxel size to compute per‑cell AABBs.
- **Neighbors**: Face‑adjacent (±X, ±Y, ±Z) vertex neighbors derived via a dense cell map for O(1) lookups.

---

### Algorithm Overview
1) **Neighbor preparation**
   - For each vertex, cache indices of up to six face neighbors using a dense `cell → vertex` map.

2) **Iterative fairing (K iterations, ping‑pong positions)**
   - For each vertex `i`:
     - Compute neighbor position average `p̄` from its face neighbors.
     - Measure material‑weight divergence with neighbors to detect transitions:
       - Let `w_i` be the weight vector at `i`, and `w_j` at neighbor `j`.
       - Use an L1 distance on weights: `d_ij = Σ_k |w_i[k] − w_j[k]|`.
       - Aggregate conservatively: `d_i = max_j d_ij`.
     - Optionally incorporate a local “material confidence” for `i`:
       - `c_i = max(w_i) − secondMax(w_i)`; lower near boundaries, higher inside homogeneous regions.
     - Derive a boundary attenuation factor `β_i ∈ [0,1]` that lowers motion near transitions:
       - Example using two thresholds `t0 < t1` (e.g., `t0=0.15`, `t1=0.35`):
         - `β_div = 1 − saturate((d_i − t0) / (t1 − t0))`
       - Optionally combine with confidence: `β_i = min(β_div, saturate(c_i / c_ref))` (e.g., `c_ref ≈ 0.4`).
     - Update position toward the average using an attenuated step: `p' = p + (α · β_i) · (p̄ − p)`.
     - Clamp `p'` to the vertex’s original cell AABB, optionally inset by a margin to avoid hugging faces:
       - `margin = cellMargin · voxelSize`.
       - `p' = clamp(p', cellMin + margin, cellMax − margin)`.

3) **Normals (optional)**
   - If desired, recompute vertex normals after the final iteration for consistent shading.

---

### Pseudocode
```csharp
for iter in 1..K:
  parallel for each vertex i:
    var neighbors = N(i)  // face-adjacent
    float3 avg = Average(positions[j] for j in neighbors)

    float dMax = 0;
    for j in neighbors:
      float d = L1Distance(weights[i], weights[j]); // sum(abs(wi - wj))
      dMax = max(dMax, d);

    float betaDiv = 1 - saturate((dMax - t0) / (t1 - t0));
    float conf = MaxMinusSecondMax(weights[i]);
    float betaConf = saturate(conf / cRef); // optional
    float beta = min(betaDiv, betaConf);    // or just betaDiv

    float3 p = positions[i] + (alpha * beta) * (avg - positions[i]);
    positionsOut[i] = ClampToCellBounds(p, cell[i], voxelSize, cellMargin);

  swap(positionsIn, positionsOut)
```

---

### Why This Preserves Sharp Transitions with Blended Materials
- **Weight divergence as boundary signal**: Large differences in weight vectors across neighbors indicate transitions. Attenuating steps where divergence is high limits cross‑boundary smoothing.
- **Cell clamping**: Constraining motion to the original cell’s AABB prevents vertices from drifting across cell boundaries and helps retain thin/small features.
- **Confidence modulation (optional)**: Reduces smoothing where the local mixture is ambiguous (low confidence), which typically occurs near transitions.

---

### Parameters and Suggested Defaults
- **Iterations `K`**: 3–10. Start with 5.
- **Base step `α`**: 0.4–0.7. Start with 0.6.
- **Divergence thresholds `t0`, `t1`**: 0.15 and 0.35 for L1 distance on weights.
- **Confidence reference `c_ref`**: ~0.4 (optional term; disable if not needed).
- **Cell margin**: 0.05–0.15 of voxel size. Start with 0.1.

---

### Implementation Notes
- Read material weights from vertex color (RGBA). Normalize if necessary so `Σw ≈ 1` to stabilize divergence.
- Use face neighbors only; this keeps the stencil small and stable for Surface Nets.
- Precompute neighbor lists once; reuse across iterations. Use ping‑pong position buffers.
- If performance is critical, the divergence test can be simplified:
  - Use dominant material index `argmax(w)` as a proxy label and treat any neighbor with a different dominant material as a boundary (cheap but coarser).
  - Or precompute `hash(w)` into 8–16 bins to approximate groups and detect cross‑bin transitions.

---

### Relation to JCGT Multi‑Label SurfaceNets
The JCGT method preserves boundaries by reducing smoothing across discrete label changes and constraining vertices to their cells. We adopt the same principles but replace discrete label equality with a continuous boundary measure based on material‑weight divergence (and optional confidence). This retains the visual sharpness of material transitions while allowing smoothing within homogeneous regions.


