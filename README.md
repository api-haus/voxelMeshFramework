![2025-08-31 at 11.20.52@2x.png](.github/images/2025-08-31%20at%2011.20.52%402x.png)

## Voxel Mesh Framework ✨

Fast, realtime voxel meshing for Unity — powered by Burst, Entities, and a clean hybrid workflow that plays nicely with GameObjects. Build editable voxel worlds that run great on desktop and mobile. 🚀🧊

### Highlights
- **Performance-first**: Naïve Surface Nets with SIMD (SSE/NEON) and Burst-compiled jobs. 🧮
- **Realtime editing**: Stamp and modify volumes at high FPS, suitable even for mobile. 📱
- **Hybrid architecture**: ECS core with GameObject render/collider bridges. Only PhysX is supported. 🧩
- **Seamless worlds**: Single meshes or stitched grids with apron copying for smooth chunk borders. 🧵
- **Materials**: Up to 4-way blended materials per vertex via RGBA corner-sum weights. 🎨
- **Normals**: Gradient, geometry-based, or none — pick what fits your pipeline. 💡
- **Background-threaded**: all voxel operations run off the main thread with configurable budgets. 🧵⚙️
- **Configurable voxel size**: enables smooth voxel operations and tuning the quality vs memory tradeoff. 📏

### What sets it apart
- **ECS inside, GO outside**: Author with familiar GameObjects while the heavy lifting stays in Entities.
- **Tunable quality vs speed**: Swap schedulers (basic or faired) and tweak normals/materials as needed.
- **Ready-to-extend**: Clear scheduling interfaces and well-factored jobs for custom pipelines.

---

## Platforms
- 🖥️🎮 Linux, Windows, consoles: Burst, Jobs, full SIMD 
- 📱🍎 iOS, Android, macOS: Burst, Jobs, partial ARM NEON SIMD 
- 🌐 WebGL 2: no Burst, no Jobs — still works great even with realtime edits 

## Requirements
- Unity DOTS stack with Entities 1.3.x
- Burst 1.8.x, Collections 2.5.x, Mathematics 1.3.x, Unity Logging 1.3.x
- PhysX (GameObject physics) for colliders

These are declared in the embedded package manifest and will be resolved by Package Manager.

---

## Features
- **Meshing algorithms**
  - Naïve Surface Nets (fast path)
  - Naïve Surface Nets with Surface Fairing (sharp details preserved)
  - Dual Contouring (planned)
  - Marching Cubes (planned)

- **Surface fairing pipeline** (post-process)
  - Precomputes neighbors, iteratively smooths, enforces in-cell constraints, and preserves material boundaries.
  - Optional normals recompute after fairing.

- **Materials and normals**
  - Blended corner-sum RGBA weights for up to 4 materials per vertex.
  - Normals modes: None, Gradient (fast), Triangle Geometry (higher quality).

- **Grids and continuity**
  - Works with single meshes or seamless grids.
  - Optional post-mesh apron copy improves cross-chunk continuity.

- **Tooling and diagnostics**
  - Job fencing/orchestration utilities and profiling hooks with extensive markers via `VoxelProfiler`.
  - Optional visual debugging (ALINE) when present in your project.

---

## Installation
Choose one of the following:

1) **Add as an embedded package** (recommended during development)
- Place/clone this repository somewhere, then in Unity open Package Manager → + -> Add from disk, and select `Packages/com.voxelmeshframework/`.

2) **Add from Git URL (UPM)**
- In Unity Package Manager: click the + button → "Add package from git URL..."
- `https://github.com/api-haus/voxelMeshFramework.git?path=Packages/com.voxelmeshframework`

---

## Quick start
1. Create a GameObject and add `VoxelMesh` (single mesh) or `VoxelMeshGrid` (tiled world).
2. In Project Settings → Voxel Mesh Framework (or via components), select the meshing algorithm and normals mode.
3. At runtime, modify the volume using stamps:

```6:17:Packages/com.voxelmeshframework/Runtime/VoxelAPI.cs
public static class VoxelAPI
{
  public static void Stamp(NativeVoxelStampProcedural stamp)
  {
    if (!VoxelEntityBridge.TryGetEntityManager(out var em))
      return;

    var ent = em.CreateEntity(typeof(NativeVoxelStampProcedural));

    em.SetComponentData(ent, stamp);
  }
}
```

---

## Algorithms and scheduling
- **Schedulers** swap algorithms at runtime via a small interface, so you can use the fastest path for colliders and the faired path for visuals.
- **Chunk size**: SIMD optimizations expect chunk size 32.
- **Fairing**: Extracts vertex data → computes neighbors → iterates fairing → optional normals update.

---

## Performance, profiling, and tests
- **Fully-bursted runtime**: Meshing, fairing, and utility jobs are Burst-compiled end to end for maximum throughput on desktop and mobile. ⚡
- **Extensive profiling**: Fine-grained Unity Profiler markers via `VoxelProfiler` cover meshing, fairing, allocation, stamping, procedural generation, spatial queries, hybrid bridging, and mesh upload/apply — enabling quick hot-path analysis. 📊
- **Performance tests**: Included tests measure meshing and end-to-end pipelines to track regressions and validate improvements across versions. ⏱️

---

## Roadmap
- File-backed chunk storage for persistence and streaming of voxel chunks.
- Rolling grids with floating world origin for pseudo-infinite procedural worlds.
- Dual Contouring implementation (Hermite/QEF) for exact sharp features.
- Marching Cubes as a compatibility baseline.
- Flexible multi-material encoding in vertex channels: selectable 4/8/12/16 materials, plus a stylized color-only mode with per-material custom colors.
- Editor authoring: procedural scene stamps and mesh voxelization for complex voxel levels in-editor.
- Seamless mesh destruction: embed voxels inside arbitrary meshes with partial voxelization at runtime.
- Additional samples and authoring tools.

---

## Screenshots / Samples
- Sample controllers and demo scenes are included under `Samples`.

---

## ⚠️ Disclaimer
> Project is in early development stage.

---

## Licenses
The Project is released under MIT License.

### Third-Party
- Fast Naïve Surface Nets by bigos91 (`https://github.com/bigos91/fastNaiveSurfaceNets`) [MIT]
	- Added partial support for ARM NEON / Apple Silicon
- Starter Assets: Character Controllers by Unity Technologies (`https://assetstore.unity.com/packages/essentials/starter-assets-character-controllers-urp-267961`)

#### Used in Demo Scenes / Samples
- Stylized textures from `https://freestylized.com` (Royalty Free License)
- Gradient Skybox `https://github.com/aadebdeb/GradientSkybox` [MIT]
- Simple URP Fog `https://github.com/meryuhi/URPFog` [MIT]
- MiniBokeh `https://github.com/keijiro/MiniBokeh` [Unlicense, MIT]

### Used Internally (optional)
- ALINE by Aron Granberg (`https://assetstore.unity.com/packages/tools/gui/aline-162772`) — conditional, excluded from distribution; used for debug gizmos only. No ALINE code is included.

---

## Note on AI usage
A GPT was used to:

- Advance technical specification development
- Implement minor or routine changes to codebase
- Assist in debugging
- Write documentation summary-style comments

---

If you find this useful, consider watching the repo or leaving a ⭐ to follow updates. Thank you! 🙏
