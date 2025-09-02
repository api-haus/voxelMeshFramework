## Surface Nets Performance Testing Plan

### Manual Check
- Packages: com.unity.test-framework.performance (to be added), com.unity.burst 1.8.24, com.unity.mathematics 1.3.2, com.unity.collections 2.5.7, com.unity.entities 1.3.14
- Docs reviewed (Unity Performance Testing):
  - Getting started, Measure.Method, Measure.Frames, Measure.Scope, ProfilerMarkers, attributes ([docs index](https://github.com/needle-mirror/com.unity.test-framework.performance), measure-method, measure-frames, test-attributes, writing-tests)
- Key guidance:
  - Use `[Test, Performance]` for method timing; configure warmup/iterations/measurements for stability
  - For frame-based/marker sampling use `[UnityTest, Performance]` and `Measure.Frames()`
  - Add assembly reference to `Unity.PerformanceTesting` in test `.asmdef`

### Goals
- Measure execution time of `NaiveSurfaceNets` job on canonical 32³ chunks for representative SDF/material inputs
- Report vertices/sec, triangles/sec, total time per chunk; compare variants (normals on/off)
- Optional: marker-based PlayMode sampling around full meshing system

### Test Matrix
- Variants:
  - recalculateNormals: false, true
- Datasets (32³):
  - Sphere SDF centered, material solid (all corners non-air near surface)
  - Plane SDF (single crossing), material solid
  - Procedural noise SDF (use existing `SimpleNoiseVoxelGenerator`), mixed materials

### Method Tests (EditMode)
- API: `Measure.Method(() => RunJobOnce(dataset, variant)).WarmupCount(5).IterationsPerMeasurement(10).MeasurementCount(10).GC().Run();`
- Custom metrics:
  - `Measure.Custom("Vertices", verticesCount)`
  - `Measure.Custom("Triangles", indicesCount/3)`
  - Derive vertices/sec and triangles/sec offline from Time sample

### Frame/Marker Tests (optional, PlayMode)
- `[UnityTest, Performance]` with `Measure.Frames()` over a configured number of frames while scheduling meshing in a scene, `DontRecordFrametime()` + `ProfilerMarkers()` as needed

### Setup Requirements
- Add dependency to `Packages/manifest.json`:
  - `"com.unity.test-framework.performance": "3.1.0"` (or project-supported latest)
- Ensure test assemblies reference `Unity.PerformanceTesting`

### CI Invocation
- Command-line export:
  - `-runTests -batchmode -projectPath <path> -testResults <path>/results.xml -perfTestResults <path>/perfResults.json`

### Notes
- Preserve current material encoding (corner-sum RGBA) per smooth voxel mapping article ([Smooth Voxel Mapping](https://bonsairobo.medium.com/smooth-voxel-mapping-a-technical-deep-dive-on-real-time-surface-nets-and-texturing-ef06d0f8ca14))
- Keep chunk size at 32 as required by job
- Use `Allocator.TempJob` for test allocations and dispose deterministically

## Implementation Ideas (Optimizations)

This section lists specific, concrete optimization ideas for `NaiveSurfaceNets` (and closely related code), including precise locations, intended changes, rationale, and validation strategy.

### 1) Branch-lean edge iteration in vertex placement (Implemented)
- Location: `Core/ThirdParty/SurfaceNets/NaiveSurfaceNets.cs` → `GetVertexPositionFromSamples`
- Changes:
  - Replace 12 unconditional edge-branches with an "iterate set bits" loop over `edgeMask` (extract lowest set bit, switch to the edge, accumulate contribution), reducing average branches and divisions to only present edges.
  - Keep linear interpolation per edge; identical math; unchanged output.
- Rationale:
  - Cuts unpredictable branching in the hottest geometric step; reduces instruction count; improves IL for Burst to optimize.
- Expected impact: Small but consistent reduction in per-cube time, especially with noisy SDFs.
- Risks: None functionally; code size is contained (12-case switch). Division-by-zero remains impossible because edges are processed only when crossing exists.
- Validation:
  - Compare `Time` sample between baseline and optimized for Sphere/Plane (normals on/off). Confirm identical vertex/index counts and CRC equivalence of index order.

### 2) Hoist architecture feature gating out of inner loops
- Location: `NaiveSurfaceNets.ProcessVoxels` and `ExtractSignBitsAndSamples`
- Changes:
  - Move `IsSse*/IsNeonSupported` branching out of the per-iteration hot path. Select implementation once before entering the XY/Z loops (e.g., local function pointers via static branches guarded outside loops; or duplicate tight blocks guarded by a single outer if/else).
  - Promote shuffle mask (`shuffleReverseByteOrder`) to `static readonly v128` (class-level).
- Rationale: Removes predictable branches inside inner loops; improves vectorizer freedom; may reduce code pipeline bubbles.
- Expected impact: Moderate; inner loop is extremely hot.
- Risks: Code size increase if we duplicate lanes. Keep duplication minimal (load/shuffle/store blocks only).
- Validation: Perf delta on Plane dataset (normals off) best highlights memory path; ensure identical outputs.

### 3) Unroll the 3-iteration triangulation loop in `MeshSamples`
- Location: `NaiveSurfaceNets.MeshSamples` (the `for (i=0;i<3;i++)` directions loop)
- Changes:
  - Manually unroll X/Y/Z cases. Precompute `du/dv` offsets with a tiny table and select via direct code blocks instead of loop/index math.
  - Keep boundary checks; annotate with `Hint.Likely` for skip paths.
- Rationale: Removes loop/indexing overhead and a data-dependent branch; aids instruction scheduling.
- Expected impact: Small-to-moderate (executed for each surface quad creation).
- Risks: Code size growth; keep the three straight-line blocks tight.
- Validation: Compare triangles count and winding; ensure normals are unaffected.

### 4) Samples in registers, not a stack array (no pointer indexing)
- Location: `ProcessVoxels` (per-cube extraction feeding `MeshSamples`) and helpers
- Changes:
  - Replace `stackalloc float[8]` with eight local `float` scalars (e.g., `s0..s7`). Pass to `GetVertexPositionFromSamples` by ref via a small `readonly ref struct` wrapper or overload taking eight floats.
- Rationale: Avoids stack addressing/pointer arithmetic; encourages register allocation for samples.
- Expected impact: Small but broad across all cubes.
- Risks: Slightly more verbose code; ensure no ABI regressions.
- Validation: Same vertex positions and counts; perf delta on all datasets.

### 5) Material corner-sum: inline, branch-lean accumulation
- Location: `GetVertexMaterialWeightsCornerSum_Interleaved`
- Changes:
  - Remove the inner local method; inline eight accumulations.
  - Use `ch = (mat - 1) & 3` and accumulate into `w0..w3` with minimal branching. Optionally replace `switch` with tiny LUT or boolean-to-int selects (Burst-friendly) to avoid the branchy switch.
  - Add `[MethodImpl(MethodImplOptions.AggressiveInlining)]`.
- Rationale: Avoid closure/local function overhead; improve predictability and inlining.
- Expected impact: Small; hot but lightweight.
- Risks: None if semantics preserved (skip AIR material only).
- Validation: Color channels distribution identical to tests in `MaterialsAndFairingTests`.

### 6) Branch shaping and hints in hot paths
- Location: `ProcessVoxels` (zerosOnes skip, cornerMask == 0/255 skip), `MeshSamples` (boundary checks), `RecalculateNormals` (degenerate triangle handling)
- Changes:
  - Use `Unity.Burst.CompilerServices.Hint.Likely/Unlikely` to annotate common skip cases (no crossings, boundaries) and rare error cases (NaN normals).
- Rationale: Helps the compiler/CPU branch prediction; minor but safe.
- Expected impact: Small.
- Risks: None.
- Validation: No functional change; observe perf noise reduction.

### 7) Add `[MethodImpl(MethodImplOptions.AggressiveInlining)]` to tiny hot helpers
- Location: `ExtractSignBitsAndSamples`, `MeshSamples`, `GetVertexPositionFromSamples`, `GetVertexNormalFromSamples`, `GetVertexMaterialWeightsCornerSum_Interleaved`
- Changes: Add inlining attribute.
- Rationale: Burst usually inlines, but the hint can help; removes call overhead and improves optimization context.
- Expected impact: Small.
- Risks: Code size; acceptable for tiny methods.
- Validation: Build OK; perf consistent or better.

### 8) Optional: Replace switch with table for edge-to-corner mapping
- Location: `GetVertexPositionFromSamples`
- Changes:
  - Use two static readonly `byte[12]` arrays giving corner indices `(a,b)` per edge and three static base offsets for coordinate axes, so each set-bit iteration does: fetch `(a,b)`, compute `t = s[a]/(s[a]-s[b])`, add axis-specific vector.
- Rationale: Table lookup can be faster than a large switch and reduces code size; improves branch predictability.
- Expected impact: Small-to-moderate.
- Risks: Careful correctness on mapping; harden with unit tests.
- Validation: Compare positions with current method bitwise-equal on float math within tolerance.

### 9) RecalculateNormals micro-optimizations
- Location: `RecalculateNormals`
- Changes:
  - In-place accumulation remains; annotate degenerate cases with `Hint.Unlikely`.
  - Consider batching more indices per iteration (e.g., process two quads) to amortize pointer fetches, keeping code readable.
- Rationale: Lower overhead in quad loop; minor gains.
- Expected impact: Small when `recalculateNormals=true`.
- Risks: Code complexity; keep minimal.
- Validation: Normals normalized and finite; visual sanity and tests.

### 10) Early arch-path selection
- Location: `ProcessVoxels`, `ExtractSignBitsAndSamples`
- Changes:
  - Choose NEON/SSE/SSE2/SSE4.1 path outside the main Z loop (e.g., split small loader/shuffler blocks under one-time branch). Keep a single path active for the run.
- Rationale: Removes repeated intrinsic-path branching.
- Expected impact: Small-to-moderate.
- Risks: Duplication; guard with clear comments.
- Validation: Perf stable across ARM/x64; identical outputs.

## Implementation Order
1) (Done) Branch-lean edge iteration in `GetVertexPositionFromSamples`.
2) Hoist arch checks + static shuffle mask.
3) Unroll `MeshSamples` direction loop.
4) Registerize cube samples (remove stack array).
5) Corner-sum inlining/branch-lean.
6) Hints + inlining attributes across helpers.
7) Optional edge mapping LUT (if switch remains a hotspot).
8) Minor normals loop improvements.

## Benchmarking & Acceptance Criteria
- Use `run_performance_tests.sh` with default filter `Voxels.Tests.Editor.NaiveSurfaceNetsPerformanceTests`.
- For each step, record `Time` median/avg and ensure vertices/triangles counts unchanged.
- Accept a change if median `Time` improves by ≥3% on at least one dataset without regressing others beyond noise; otherwise revert or adjust.

## Tooling Notes
- Performance JSON: current Unity run embeds results in `results.xml` as `##performancetestresult2` blocks. If `-perfTestResults` does not emit a file, extend the shell script to extract these JSON blocks from `results.xml` into `perfResults.json` post-run.

