![2025-08-31 at 11.20.52@2x.png](.github/images/2025-08-31%20at%2011.20.52%402x.png)

## Voxel Mesh Framework ✨

Fast, realtime voxel meshing for Unity — powered by Burst, Entities, and a clean hybrid workflow that plays nicely with
GameObjects. Build editable voxel worlds that run great on desktop and mobile. 🚀🧊

Join our [Discord Community](https://discord.gg/htrA8H3n)!

---

## Platforms

- 🖥️🎮 Linux, Windows, consoles: Burst, Jobs, full SIMD
- 📱🍎 iOS, Android, macOS: Burst, Jobs, partial ARM NEON SIMD
- 🌐 WebGL 2: no Burst, no Jobs — still works great even with realtime edits

## Requirements

- Unity DOTS stack with Entities 1.3.x
- Burst 1.8.x, Collections 2.5.x, Mathematics 1.3.x, Unity Logging 1.3.x
- PhysX (GameObject physics) for colliders

Future: ecs graphics, ecs physics

---

## Installation

Choose one of the following:

1) **Add as an embedded package** (recommended during development)

- Place/clone this repository somewhere, then in Unity open Package Manager → + -> Add from disk, and select
	package path.

2) **Add from Git URL (UPM)**

- In Unity Package Manager: click the + button → "Add package from git URL..."
- `https://github.com/api-haus/voxelMeshFramework.git`

---

## Quick start

1. Create a GameObject and add `VoxelMesh` (single mesh) or `VoxelMeshGrid` (tiled world).
2. Add a Procedural generator: one of [`Core/Procedural/Generators`](Core/Procedural/Generators)
3. At runtime, modify the volume using
	 stamps: [`SampleFirstPersonDiggingCamera`](Samples/SampleControllers/SampleFirstPersonDiggingCamera.cs)

---

## Roadmap

### Next steps

- Sync/Async switch in dynamic budget adjustments
- Voxel queries
- Flexible multi-material encoding in vertex channels: selectable 4/8/12/16 materials, plus a stylized color-only mode
	with per-material custom colors.
- Improve the public stamp API.
- Extensive scene authoring with both procedural shapes and voxelised mesh geometry.
- Investigate 3D clipmap-based LOD with toroidal updates, Octree LOD w/ seams fixing.
- File-backed chunk storage for persistence and streaming of voxel chunks.
- Rolling grids with floating world origin for pseudo-infinite procedural worlds.
- Editor authoring: procedural scene stamps and mesh voxelization for complex voxel levels in-editor.
- Seamless mesh destruction: embed voxels inside arbitrary meshes with partial voxelization at runtime.
- Additional samples and authoring tools.
- Dual Contouring implementation (Hermite/QEF) for exact sharp features.
- Marching Cubes as a compatibility baseline.

---

## Screenshots / Samples

- Sample controllers and demo scenes are included under [`Samples/`](Samples/).

---

## ⚠️ Disclaimer

> Project is in early development stage.

---

## Licenses

The Project is released under MIT License.

### Third-Party

- Fast Naïve Surface Nets by bigos91 (`https://github.com/bigos91/fastNaiveSurfaceNets`) [MIT]
	- Added partial support for ARM NEON / Apple Silicon

---

## Note on AI usage

A GPT was used to:

- Advance technical specification development
- Implement minor or routine changes to codebase
- Assist in debugging
- Write documentation summary-style comments

---

If you find this useful, consider watching the repo or leaving a ⭐ to follow updates. Thank you! 🙏
