# Material Contouring Implementation

This document describes the material contouring technique implemented to address banding artifacts in voxel surface rendering when materials are encoded in vertex colors.

## Problem Statement

When encoding material IDs and blend weights in vertex colors and relying on linear interpolation across triangles, visible banding artifacts occur at material boundaries due to:
- Sharp transitions between different materials
- Linear interpolation not respecting surface features
- Lack of anti-aliasing at material boundaries

## Solution: Distance Field Material Contouring

The implementation uses a distance field approach to create smooth, anti-aliased material transitions while minimizing texture samples.

### Key Components

#### 1. Vertex Color Encoding (MesherJob.cs)

The vertex color channels are used as follows:
- **R channel**: Primary material ID (0-255)
- **G channel**: Secondary material ID (0-255)
- **B channel**: Blend weight between materials (0-255, where 0 = mat0, 255 = mat1)
- **A channel**: Distance to nearest material boundary (0-255, where 0 = at boundary, 255 = far from boundary)

#### 2. Material Analysis Algorithm

The `GetVertexMaterialInfo` method in MesherJob.cs:

1. **Material Sampling**: Samples materials at all 8 corners of each voxel cube
2. **Dominant Material Selection**: Identifies the two most common materials in the cube
3. **Inverse Distance Weighting**: Calculates blend weights using inverse distance from vertex to each corner
4. **Boundary Distance Calculation**: Computes the minimum distance from the vertex to any material boundary edge

```csharp
// Calculate blend weight using inverse distance weighting
var totalWeight0 = 0f;
var totalWeight1 = 0f;

for (var i = 0; i < 8; i++)
{
    var corner = new float3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
    var dist = length(corner - vertexOffset);
    var weight = 1f / (dist + 0.001f); // Avoid division by zero
    
    if (cornerMaterials[i] == mat0)
        totalWeight0 += weight;
    else if (cornerMaterials[i] == mat1)
        totalWeight1 += weight;
}

var blendWeight = totalWeight1 / (totalWeight0 + totalWeight1 + 0.001f);
```

#### 3. Shader Enhancement (Voxel Surface.surfshader)

The shader uses the distance field information to create smooth transitions:

1. **Height-Based Blending**: Modulates blend weight based on texture height differences
   ```hlsl
   half heightDiff = (height1 - height0) * heightContrast;
   half distanceModulation = saturate(boundaryDist * 3.0h);
   w = saturate(w + heightDiff * distanceModulation * 0.5h);
   ```

2. **Anti-Aliased Transitions**: Uses screen-space derivatives for smooth edges
   ```hlsl
   half2 dwdx = ddx(half2(w, boundaryDist));
   half2 dwdy = ddy(half2(w, boundaryDist));
   half fwidth_w = length(half2(dwdx.x, dwdy.x)) * 2.0h;
   
   half transitionWidth = max(0.01h, fwidth_w * _BlendContrast);
   half aaBlend = smoothstep(0.5h - transitionWidth, 0.5h + transitionWidth, w);
   ```

3. **Enhanced Seam Effect**: Material seams now follow the actual material boundaries
   ```hlsl
   half seamDistance = 1.0h - boundaryDist;
   half seamFalloff = pow(seamDistance, 2.0h);
   half seamGradient = length(half2(ddx(seamDistance), ddy(seamDistance)));
   half seamAA = saturate(1.0h / max(0.001h, seamGradient * _SeamWidth * 10.0h));
   ```

## Benefits

1. **Smooth Transitions**: Eliminates harsh banding at material boundaries
2. **Surface-Aware Blending**: Respects height/displacement maps for natural transitions
3. **Anti-Aliasing**: Screen-space derivatives provide proper edge smoothing
4. **Performance**: Minimal texture sampling overhead (same number of samples as before)
5. **Artistic Control**: Shader parameters allow fine-tuning of transition behavior

## Usage

The system works automatically with the existing voxel mesh generation pipeline. Artists can control the appearance using these shader parameters:

- `_BlendContrast`: Controls the sharpness of material transitions
- `_ColorBlendContrast`: Influences height-based blending strength
- `_SeamWidth` and `_SeamIntensity`: Control material boundary seam appearance

## Technical Notes

- The distance field is computed in the meshing job at vertex creation time, adding minimal overhead
- The approach is compatible with both biplanar and triplanar texture mapping
- Works seamlessly with parallax mapping and vertex displacement
- The technique scales well with different voxel resolutions
