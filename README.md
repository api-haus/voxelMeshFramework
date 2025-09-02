![2025-08-31 at 11.20.52@2x.png](.github/images/2025-08-31%20at%2011.20.52%402x.png)

Features fast naive surface nets with SIMD for wide range of platforms.

Fully realtime edits at high FPS, suitable for mobile platforms.

Backed by Entities, controlled with GameObjects, supports only PhysX (Game Object physics).

Supports singular voxel meshes and seamless grids with variable voxel size.

### ⚠️ Disclaimer

> Project is in early development stage.

## Licenses

The Project is released under MIT License.

### Third-Party

* Fast Naive Surface Nets by bigos91 https://github.com/bigos91/fastNaiveSurfaceNets [MIT]
	* Added partial support for ARM NEON / Apple Silicon
* [MicroSplat Core](https://assetstore.unity.com/packages/tools/terrain/microsplat-96478)
	by [Jason Booth](http://jdbtechservices.com/) is a Free Unity Asset
	* Distributed
		under [Unity Package Distribution License](https://unity.com/legal/licenses/unity-package-distribution-license)
	* Utilised to generate packed Texture Arrays
	* Will be removed once Visual Material system is developed
* Starter Assets: Character Controllers by Unity
	Technologies https://assetstore.unity.com/packages/essentials/starter-assets-character-controllers-urp-267961

#### Used in Demo Scenes / Samples

* Stylized textures obtained from https://freestylized.com
	* Released under a Royalty Free License
* Gradient Skybox https://github.com/aadebdeb/GradientSkybox [MIT]

### Used Internally

* [ALINE](https://assetstore.unity.com/packages/tools/gui/aline-162772)
	by [Aron Granberg](https://www.arongranberg.com) [Conditional, Excluded from distribution]
	* Conditionally imported, provides debug gizmos
	* None of the code provided by ALINE is included in project.
* [Better Shaders 2022](https://assetstore.unity.com/packages/tools/visual-scripting/better-shaders-2022-standard-urp-hdrp-244057)
	by [Jason Booth](http://jdbtechservices.com/) [Conditional, Excluded from distribution]
	* Used to author Voxel Surface Shaders.
	* None of the code provided by Better Shaders 2022 is included in project.

### Note on AI usage

A GPT was used to:

- Advance technical specification development,
- Implement minor or routine changes to codebase,
- Assist in debugging,
- Write documentation summary style comments.
