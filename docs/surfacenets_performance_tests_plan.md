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
