# Dual Contouring Shader System Design

## Overview

This document details a comprehensive shader system designed specifically for rendering dual contouring output with multi-material support, advanced blending, and high-quality visual effects. The system leverages Unity's rendering pipeline while providing custom solutions for the unique challenges of voxel-based geometry.

## Core Requirements

### Dual Contouring Specific Needs

1. **Per-Vertex Material Data**: Encode multiple materials and blend weights
2. **Smooth Material Transitions**: Anti-aliased boundaries without texture bleeding
3. **Sharp Feature Preservation**: Maintain crisp edges where appropriate
4. **Efficient Texture Sampling**: Minimize texture lookups for performance
5. **LOD Support**: Seamless transitions between detail levels
6. **Dynamic Updates**: Support for runtime mesh modifications

### Visual Quality Goals

- **PBR Compliance**: Full physically-based rendering support
- **Triplanar Mapping**: Eliminate texture stretching on arbitrary geometry
- **Procedural Details**: Add fine details without increasing mesh complexity
- **Proper Shadows**: Self-shadowing and shadow receiving
- **Environmental Effects**: Weather, wetness, dirt accumulation

## Vertex Data Architecture

### Enhanced Vertex Format

```hlsl
// Vertex input structure optimized for dual contouring
struct DualContouringVertexInput
{
    float3 position : POSITION;       // Local space position
    float3 normal : NORMAL;           // Surface normal from QEF/triangles
    float4 tangent : TANGENT;         // xyz = material boundary normal, w = distance
    float4 color : COLOR0;            // RGBA = materials and blending data
    float4 color2 : COLOR1;           // Additional material weights/properties
    float2 texcoord : TEXCOORD0;     // Base UV (for decals/overlays)
    uint vertexID : SV_VertexID;     // For procedural effects
};

// Vertex data encoding scheme
// COLOR0:
//   R: Primary material ID (0-255)
//   G: Secondary material ID (0-255)  
//   B: Base blend factor (0-255)
//   A: Material count and flags (bits 0-3: count, bits 4-7: flags)
//
// COLOR1:
//   R: Tertiary material ID (0-255)
//   G: Quaternary material ID (0-255)
//   B: Extended blend factor
//   A: Sharp feature flag and transition type

// TANGENT:
//   XYZ: Material boundary plane normal
//   W: Signed distance to boundary (for analytic AA)
```

### Vertex Shader

```hlsl
// Optimized vertex shader for dual contouring meshes
struct DualContouringVertexOutput
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 positionOS : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
    float4 tangentWS : TEXCOORD3;
    float4 materialData0 : TEXCOORD4;  // Packed material IDs and base blend
    float4 materialData1 : TEXCOORD5;  // Extended materials and properties
    float4 boundaryData : TEXCOORD6;   // Material boundary information
    float4 screenPos : TEXCOORD7;      // Screen position for effects
    #if defined(LIGHTMAP_ON)
    float2 lightmapUV : TEXCOORD8;
    #endif
    UNITY_FOG_COORDS(9)
};

DualContouringVertexOutput DualContouringVertex(DualContouringVertexInput input)
{
    DualContouringVertexOutput output = (DualContouringVertexOutput)0;
    
    // Standard transformations
    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.position);
    output.positionCS = vertexInput.positionCS;
    output.positionWS = vertexInput.positionWS;
    output.positionOS = input.position;
    
    // Normal transformation with normalization
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normal, input.tangent);
    output.normalWS = normalInput.normalWS;
    output.tangentWS = float4(normalInput.tangentWS, input.tangent.w);
    
    // Unpack and pass material data
    output.materialData0 = input.color;
    output.materialData1 = input.color2;
    
    // Transform boundary plane to world space
    float3 boundaryNormalWS = TransformObjectToWorldNormal(input.tangent.xyz);
    float boundaryDistWS = input.tangent.w * length(
        mul((float3x3)UNITY_MATRIX_M, float3(1,0,0))
    ); // Scale distance by object scale
    output.boundaryData = float4(boundaryNormalWS, boundaryDistWS);
    
    // Screen position for screen-space effects
    output.screenPos = ComputeScreenPos(output.positionCS);
    
    #if defined(LIGHTMAP_ON)
    output.lightmapUV = input.texcoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
    #endif
    
    TRANSFER_FOG(output, output.positionCS);
    
    return output;
}
```

## Material System

### Material Array Architecture

```hlsl
// Material texture arrays (2D Array textures)
TEXTURE2D_ARRAY(_MaterialAlbedoArray);
SAMPLER(sampler_MaterialAlbedoArray);

TEXTURE2D_ARRAY(_MaterialNormalArray);
SAMPLER(sampler_MaterialNormalArray);

TEXTURE2D_ARRAY(_MaterialMaskArray); // R=Metallic, G=Smoothness, B=AO, A=Height
SAMPLER(sampler_MaterialMaskArray);

TEXTURE2D_ARRAY(_MaterialDetailArray); // Detail textures
SAMPLER(sampler_MaterialDetailArray);

// Material properties buffer
CBUFFER_START(MaterialProperties)
    float4 _MaterialScales[MAX_MATERIALS];      // XY = base scale, ZW = detail scale
    float4 _MaterialParams[MAX_MATERIALS];      // X = metallic, Y = smoothness, Z = normal strength, W = detail strength
    float4 _MaterialColors[MAX_MATERIALS];      // Tint colors
    float4 _MaterialTransitions[MAX_MATERIALS]; // Transition parameters
CBUFFER_END

// Material sampling structure
struct MaterialSample
{
    float3 albedo;
    float3 normal;
    float metallic;
    float smoothness;
    float occlusion;
    float height;
    float3 emission;
};
```

### Triplanar Sampling

```hlsl
// Optimized triplanar sampling for voxel geometry
MaterialSample SampleMaterialTriplanar(
    int materialID,
    float3 positionWS,
    float3 normalWS,
    float2 baseUV)
{
    // Calculate triplanar blend weights
    float3 blendWeights = abs(normalWS);
    blendWeights = saturate(blendWeights - 0.2);
    blendWeights = pow(blendWeights, 4);
    blendWeights /= dot(blendWeights, 1.0);
    
    // Calculate triplanar UVs
    float2 uvX = positionWS.yz * _MaterialScales[materialID].xy;
    float2 uvY = positionWS.xz * _MaterialScales[materialID].xy;
    float2 uvZ = positionWS.xy * _MaterialScales[materialID].xy;
    
    // Sample textures for each projection
    MaterialSample sampleX = SampleMaterialSingle(materialID, uvX);
    MaterialSample sampleY = SampleMaterialSingle(materialID, uvY);
    MaterialSample sampleZ = SampleMaterialSingle(materialID, uvZ);
    
    // Blend samples
    MaterialSample result;
    result.albedo = sampleX.albedo * blendWeights.x +
                    sampleY.albedo * blendWeights.y +
                    sampleZ.albedo * blendWeights.z;
    
    // Blend normals with whiteout blending
    float3 nx = float3(0, sampleX.normal.yx);
    float3 ny = float3(sampleY.normal.x, 0, sampleY.normal.y);
    float3 nz = float3(sampleZ.normal.xy, 0);
    
    float3 worldNormal = normalize(
        nx * blendWeights.x +
        ny * blendWeights.y +
        nz * blendWeights.z +
        normalWS
    );
    
    result.normal = worldNormal;
    
    // Blend other properties
    result.metallic = dot(float3(sampleX.metallic, sampleY.metallic, sampleZ.metallic), blendWeights);
    result.smoothness = dot(float3(sampleX.smoothness, sampleY.smoothness, sampleZ.smoothness), blendWeights);
    result.occlusion = dot(float3(sampleX.occlusion, sampleY.occlusion, sampleZ.occlusion), blendWeights);
    result.height = dot(float3(sampleX.height, sampleY.height, sampleZ.height), blendWeights);
    
    return result;
}

// Single projection sampling
MaterialSample SampleMaterialSingle(int materialID, float2 uv)
{
    MaterialSample sample;
    
    float4 albedo = SAMPLE_TEXTURE2D_ARRAY(_MaterialAlbedoArray, 
        sampler_MaterialAlbedoArray, uv, materialID);
    float4 normal = SAMPLE_TEXTURE2D_ARRAY(_MaterialNormalArray, 
        sampler_MaterialNormalArray, uv, materialID);
    float4 mask = SAMPLE_TEXTURE2D_ARRAY(_MaterialMaskArray, 
        sampler_MaterialMaskArray, uv, materialID);
    
    sample.albedo = albedo.rgb * _MaterialColors[materialID].rgb;
    sample.normal = UnpackNormalScale(normal, _MaterialParams[materialID].z);
    sample.metallic = mask.r * _MaterialParams[materialID].x;
    sample.smoothness = mask.g * _MaterialParams[materialID].y;
    sample.occlusion = mask.b;
    sample.height = mask.a;
    sample.emission = 0; // Set per-material if needed
    
    return sample;
}
```

## Multi-Material Blending

### Advanced Blending System

```hlsl
// Height-based blending for natural transitions
float HeightBlend(float height0, float height1, float blend, float contrast)
{
    float heightDiff = height1 - height0;
    float factor = heightDiff * contrast;
    return saturate(blend + factor);
}

// Multi-material blending with up to 4 materials
MaterialSample BlendMultiMaterial(
    DualContouringVertexOutput input,
    float3 pixelNormalWS)
{
    // Decode material IDs
    int mat0 = (int)(input.materialData0.x * 255.0);
    int mat1 = (int)(input.materialData0.y * 255.0);
    int mat2 = (int)(input.materialData1.x * 255.0);
    int mat3 = (int)(input.materialData1.y * 255.0);
    
    int materialCount = (int)(input.materialData0.w) & 0x0F;
    
    // Sample all materials
    MaterialSample samples[4];
    samples[0] = SampleMaterialTriplanar(mat0, input.positionWS, pixelNormalWS, 0);
    
    if (materialCount > 1)
        samples[1] = SampleMaterialTriplanar(mat1, input.positionWS, pixelNormalWS, 0);
    if (materialCount > 2)
        samples[2] = SampleMaterialTriplanar(mat2, input.positionWS, pixelNormalWS, 0);
    if (materialCount > 3)
        samples[3] = SampleMaterialTriplanar(mat3, input.positionWS, pixelNormalWS, 0);
    
    // Calculate blend weights with height influence
    float4 weights = float4(1, 0, 0, 0);
    
    if (materialCount > 1)
    {
        // Primary blend
        float blend01 = input.materialData0.z;
        
        // Distance to material boundary
        float boundaryDist = dot(input.positionWS, input.boundaryData.xyz) - input.boundaryData.w;
        
        // Analytic antialiasing
        float pixelSize = length(fwidth(input.positionWS));
        float transitionWidth = _MaterialTransitions[mat0].x * pixelSize;
        
        // Smooth transition with height influence
        float heightBlend = HeightBlend(samples[0].height, samples[1].height, blend01, 
                                       _MaterialTransitions[mat0].y);
        
        // Apply boundary-based transition
        float boundaryBlend = smoothstep(-transitionWidth, transitionWidth, boundaryDist);
        
        weights.x = 1.0 - boundaryBlend;
        weights.y = boundaryBlend;
        
        // Apply height-based modification
        weights.xy = lerp(weights.xy, float2(1.0 - heightBlend, heightBlend), 
                         _MaterialTransitions[mat0].z);
    }
    
    // Extended blending for 3+ materials
    if (materialCount > 2)
    {
        float blend23 = input.materialData1.z;
        weights.z = blend23 * (1.0 - weights.x - weights.y);
        weights.xy *= (1.0 - weights.z);
    }
    
    if (materialCount > 3)
    {
        float blend34 = input.materialData1.w;
        weights.w = blend34 * (1.0 - weights.x - weights.y - weights.z);
        weights.xyz *= (1.0 - weights.w);
    }
    
    // Normalize weights
    weights /= dot(weights, 1.0);
    
    // Blend materials
    MaterialSample result = (MaterialSample)0;
    
    [unroll]
    for (int i = 0; i < materialCount; i++)
    {
        float w = weights[i];
        result.albedo += samples[i].albedo * w;
        result.normal += samples[i].normal * w;
        result.metallic += samples[i].metallic * w;
        result.smoothness += samples[i].smoothness * w;
        result.occlusion += samples[i].occlusion * w;
        result.height += samples[i].height * w;
        result.emission += samples[i].emission * w;
    }
    
    // Renormalize blended normal
    result.normal = normalize(result.normal);
    
    return result;
}
```

### Seam and Edge Effects

```hlsl
// Material seam rendering for stylized looks
float3 ApplyMaterialSeam(
    float3 albedo,
    DualContouringVertexOutput input,
    float3 pixelNormalWS)
{
    // Check if near material boundary
    float boundaryDist = abs(dot(input.positionWS, input.boundaryData.xyz) - input.boundaryData.w);
    float pixelSize = length(fwidth(input.positionWS));
    
    float seamWidth = _SeamWidth * pixelSize;
    float seamMask = 1.0 - smoothstep(0, seamWidth, boundaryDist);
    
    // Apply seam color
    float3 seamColor = _SeamColor.rgb;
    float seamIntensity = _SeamIntensity * seamMask;
    
    // Darken or colorize seam
    albedo = lerp(albedo, seamColor * albedo, seamIntensity);
    
    return albedo;
}

// Sharp edge detection and enhancement
float DetectSharpEdge(float3 normalWS, DualContouringVertexOutput input)
{
    // Compare vertex normal with calculated pixel normal
    float3 pixelNormal = normalize(cross(ddx(input.positionWS), ddy(input.positionWS)));
    float normalDiff = 1.0 - dot(normalWS, pixelNormal);
    
    // Check boundary data for sharp feature flag
    float sharpFlag = step(0.5, (input.materialData0.w >> 4) & 0x01);
    
    return saturate(normalDiff * 10.0) * sharpFlag;
}
```

## Fragment Shader

### Main Fragment Function

```hlsl
float4 DualContouringFragment(DualContouringVertexOutput input) : SV_Target
{
    // Initialize material sampling data
    float3 pixelNormalWS = normalize(input.normalWS);
    
    // Blend multiple materials
    MaterialSample material = BlendMultiMaterial(input, pixelNormalWS);
    
    // Apply detail textures
    #if defined(_DETAIL_ENABLED)
    ApplyDetailTextures(material, input.positionWS, pixelNormalWS);
    #endif
    
    // Calculate final surface properties
    SurfaceData surfaceData;
    surfaceData.albedo = material.albedo;
    surfaceData.specular = float3(0.04, 0.04, 0.04);
    surfaceData.metallic = material.metallic;
    surfaceData.smoothness = material.smoothness;
    surfaceData.normalTS = material.normal;
    surfaceData.emission = material.emission;
    surfaceData.occlusion = material.occlusion;
    surfaceData.alpha = 1.0;
    
    // Apply material seams if enabled
    #if defined(_SEAM_ENABLED)
    surfaceData.albedo = ApplyMaterialSeam(surfaceData.albedo, input, pixelNormalWS);
    #endif
    
    // Sharp edge enhancement
    #if defined(_SHARP_EDGES)
    float edgeFactor = DetectSharpEdge(pixelNormalWS, input);
    surfaceData.smoothness *= (1.0 - edgeFactor * 0.5);
    #endif
    
    // Initialize input data for lighting
    InputData inputData;
    inputData.positionWS = input.positionWS;
    inputData.normalWS = NormalizeNormalPerPixel(material.normal);
    inputData.viewDirectionWS = GetWorldSpaceViewDir(input.positionWS);
    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
    inputData.fogCoord = input.fogCoord;
    inputData.vertexLighting = 0;
    inputData.bakedGI = SampleSH(inputData.normalWS);
    
    // Calculate PBR lighting
    float4 color = UniversalFragmentPBR(inputData, surfaceData);
    
    // Apply fog
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    
    return color;
}
```

### Detail Texture System

```hlsl
void ApplyDetailTextures(
    inout MaterialSample material,
    float3 positionWS,
    float3 normalWS)
{
    // Sample detail textures at higher frequency
    float2 detailUV = positionWS.xz * _DetailScale;
    
    float4 detailAlbedo = SAMPLE_TEXTURE2D_ARRAY(_MaterialDetailArray, 
        sampler_MaterialDetailArray, detailUV, 0);
    float4 detailNormal = SAMPLE_TEXTURE2D_ARRAY(_MaterialDetailArray, 
        sampler_MaterialDetailArray, detailUV, 1);
    
    // Blend detail based on distance
    float detailFade = saturate((_DetailFadeDistance - length(positionWS - _WorldSpaceCameraPos)) 
                               / _DetailFadeRange);
    
    // Apply detail
    material.albedo *= lerp(1.0, detailAlbedo.rgb * 2.0, detailFade * _DetailStrength);
    
    float3 detailNormalTS = UnpackNormalScale(detailNormal, _DetailNormalStrength * detailFade);
    material.normal = BlendNormals(material.normal, detailNormalTS);
}
```

## LOD and Performance

### LOD Shader Variants

```hlsl
// Shader keywords for LOD
#pragma shader_feature_local _ _LOD0 _LOD1 _LOD2

// Simplified material sampling for distant geometry
#if defined(_LOD2)
    // Single material, no blending
    MaterialSample material = SampleMaterialBiplanar(mat0, input.positionWS, pixelNormalWS);
#elif defined(_LOD1)
    // Two materials max, simple blend
    MaterialSample mat0Sample = SampleMaterialTriplanar(mat0, input.positionWS, pixelNormalWS, 0);
    MaterialSample mat1Sample = SampleMaterialTriplanar(mat1, input.positionWS, pixelNormalWS, 0);
    MaterialSample material = LerpMaterialSamples(mat0Sample, mat1Sample, input.materialData0.z);
#else
    // Full quality
    MaterialSample material = BlendMultiMaterial(input, pixelNormalWS);
#endif
```

### Texture LOD Bias

```hlsl
// Automatic LOD calculation based on mesh LOD
float CalculateLODBias(float3 positionWS)
{
    float distance = length(positionWS - _WorldSpaceCameraPos);
    float lodBias = 0.0;
    
    #if defined(_LOD1)
    lodBias = 1.0;
    #elif defined(_LOD2)
    lodBias = 2.0;
    #endif
    
    // Additional distance-based bias
    lodBias += saturate((distance - _LOD0Distance) / (_LOD1Distance - _LOD0Distance));
    
    return lodBias;
}

// Modified texture sampling with LOD
float4 SAMPLE_TEXTURE2D_ARRAY_LOD(
    TEXTURE2D_ARRAY_PARAM(textureName, samplerName),
    float2 uv,
    int slice,
    float lodBias)
{
    return SAMPLE_TEXTURE2D_ARRAY_BIAS(textureName, samplerName, uv, slice, lodBias);
}
```

## Advanced Effects

### Procedural Surface Details

```hlsl
// Add procedural micro-details without geometry
float3 AddProceduralDetails(
    float3 albedo,
    float3 positionWS,
    float3 normalWS,
    int materialID)
{
    // High-frequency noise for surface variation
    float noise = SimplexNoise3D(positionWS * _ProceduralScale[materialID]);
    
    // Material-specific patterns
    switch (_MaterialTypes[materialID])
    {
        case MATERIAL_TYPE_STONE:
            // Add cracks and wear
            float cracks = pow(abs(noise), 3.0) * _ProceduralStrength[materialID];
            albedo *= 1.0 - cracks * 0.3;
            break;
            
        case MATERIAL_TYPE_METAL:
            // Add rust and oxidation
            float rust = saturate(noise + normalWS.y * 0.5) * _ProceduralStrength[materialID];
            albedo = lerp(albedo, _RustColor.rgb, rust * 0.4);
            break;
            
        case MATERIAL_TYPE_ORGANIC:
            // Add growth patterns
            float growth = fbm(positionWS * _ProceduralScale[materialID], 3);
            albedo *= 1.0 + growth * _ProceduralStrength[materialID] * 0.2;
            break;
    }
    
    return albedo;
}
```

### Weather Effects

```hlsl
// Dynamic weather system integration
void ApplyWeatherEffects(
    inout SurfaceData surface,
    float3 positionWS,
    float3 normalWS)
{
    #if defined(_WEATHER_ENABLED)
    // Rain wetness
    float wetness = _GlobalWetness;
    
    // Puddle formation in crevices
    float puddleMask = saturate(1.0 - normalWS.y);
    wetness = lerp(wetness, 1.0, puddleMask * _PuddleAmount);
    
    // Modify surface properties
    surface.albedo = lerp(surface.albedo, surface.albedo * 0.7, wetness);
    surface.smoothness = lerp(surface.smoothness, 0.95, wetness);
    surface.metallic *= (1.0 - wetness * 0.5);
    
    // Snow accumulation
    float snowMask = saturate(normalWS.y - 0.5) * 2.0;
    float snowAmount = _GlobalSnow * snowMask;
    
    surface.albedo = lerp(surface.albedo, _SnowColor.rgb, snowAmount);
    surface.smoothness = lerp(surface.smoothness, _SnowSmoothness, snowAmount);
    #endif
}
```

### Subsurface Scattering Approximation

```hlsl
// Fast subsurface scattering for organic materials
float3 SubsurfaceScattering(
    float3 albedo,
    float3 normalWS,
    float3 viewDirWS,
    float3 lightDirWS,
    float thickness,
    float3 scatterColor)
{
    // Simple transmission
    float VdotL = saturate(dot(viewDirWS, -lightDirWS));
    float transmission = pow(VdotL, _ScatterPower) * thickness;
    
    // Wrap lighting for soft appearance
    float NdotL = dot(normalWS, lightDirWS);
    float wrapped = (NdotL + _WrapAmount) / (1.0 + _WrapAmount);
    
    // Combine
    float3 scatter = scatterColor * (transmission + wrapped * 0.5);
    return albedo + scatter * _ScatterStrength;
}
```

## Integration with Unity Features

### Shader Graph Integration

```hlsl
// Custom function node for Shader Graph
void DualContouringMaterialBlend_float(
    float4 MaterialData0,
    float4 MaterialData1,
    float4 BoundaryData,
    float3 PositionWS,
    float3 NormalWS,
    out float3 Albedo,
    out float3 Normal,
    out float Metallic,
    out float Smoothness,
    out float AO)
{
    #ifdef SHADERGRAPH_PREVIEW
    Albedo = float3(0.5, 0.5, 0.5);
    Normal = float3(0, 0, 1);
    Metallic = 0;
    Smoothness = 0.5;
    AO = 1;
    #else
    // Full implementation
    DualContouringVertexOutput input;
    input.materialData0 = MaterialData0;
    input.materialData1 = MaterialData1;
    input.boundaryData = BoundaryData;
    input.positionWS = PositionWS;
    input.normalWS = NormalWS;
    
    MaterialSample material = BlendMultiMaterial(input, NormalWS);
    
    Albedo = material.albedo;
    Normal = material.normal;
    Metallic = material.metallic;
    Smoothness = material.smoothness;
    AO = material.occlusion;
    #endif
}
```

### VFX Graph Support

```hlsl
// Vertex data for VFX Graph particles
struct DualContouringVFXData
{
    float3 position;
    float3 normal;
    float3 tangent;
    float4 materialData;
    float surfaceHeight;
};

// Sample surface for particle emission
DualContouringVFXData SampleSurfaceForVFX(
    float2 uv,
    DualContouringVertexInput vertex)
{
    DualContouringVFXData data;
    
    // Interpolate vertex data
    data.position = vertex.position;
    data.normal = vertex.normal;
    data.tangent = vertex.tangent.xyz;
    data.materialData = vertex.color;
    
    // Sample height at position
    MaterialSample material = SampleMaterialTriplanar(
        (int)(vertex.color.x * 255.0),
        TransformObjectToWorld(vertex.position),
        TransformObjectToWorldNormal(vertex.normal),
        uv
    );
    
    data.surfaceHeight = material.height;
    
    return data;
}
```

## Performance Optimization

### Shader Variants and Keywords

```hlsl
// Shader feature toggles
#pragma shader_feature_local _ _NORMALMAP
#pragma shader_feature_local _ _PARALLAXMAP
#pragma shader_feature_local _ _EMISSION
#pragma shader_feature_local _ _DETAIL_ENABLED
#pragma shader_feature_local _ _WEATHER_ENABLED
#pragma shader_feature_local _ _SEAM_ENABLED
#pragma shader_feature_local _ _SHARP_EDGES
#pragma shader_feature_local_fragment _ _MATERIAL_BLEND_TWO _MATERIAL_BLEND_THREE _MATERIAL_BLEND_FOUR

// Multi-compile for lighting
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
#pragma multi_compile_fragment _ _SHADOWS_SOFT
#pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
```

### Instancing Support

```hlsl
// Instanced properties for dynamic chunks
UNITY_INSTANCING_BUFFER_START(DualContouringProps)
    UNITY_DEFINE_INSTANCED_PROP(float4, _ChunkPosition)
    UNITY_DEFINE_INSTANCED_PROP(float4, _ChunkMaterialRemap)
    UNITY_DEFINE_INSTANCED_PROP(float, _ChunkLOD)
UNITY_INSTANCING_BUFFER_END(DualContouringProps)

// Apply per-instance data
void ApplyInstanceData(inout DualContouringVertexInput input)
{
    #ifdef UNITY_INSTANCING_ENABLED
    float4 chunkPos = UNITY_ACCESS_INSTANCED_PROP(DualContouringProps, _ChunkPosition);
    input.position += chunkPos.xyz;
    
    // Remap materials for chunk variations
    float4 remap = UNITY_ACCESS_INSTANCED_PROP(DualContouringProps, _ChunkMaterialRemap);
    input.color.x = lerp(input.color.x, remap.x, remap.w);
    input.color.y = lerp(input.color.y, remap.y, remap.w);
    #endif
}
```

## Debugging and Visualization

### Debug Visualization Modes

```hlsl
// Debug output modes
#if defined(_DEBUG_MODE)
float4 DebugOutput(DualContouringVertexOutput input, MaterialSample material)
{
    switch (_DebugMode)
    {
        case 1: // Material IDs
            return float4(
                input.materialData0.x,
                input.materialData0.y,
                input.materialData1.x,
                1.0
            );
            
        case 2: // Blend weights
            return float4(
                input.materialData0.z,
                input.materialData1.z,
                0,
                1.0
            );
            
        case 3: // Normals
            return float4(input.normalWS * 0.5 + 0.5, 1.0);
            
        case 4: // Material boundaries
            float boundaryDist = abs(dot(input.positionWS, input.boundaryData.xyz) - input.boundaryData.w);
            float boundary = 1.0 - saturate(boundaryDist * 10.0);
            return float4(boundary, 0, 0, 1.0);
            
        case 5: // Triplanar blend
            float3 blend = abs(input.normalWS);
            blend = pow(blend, 4);
            blend /= dot(blend, 1.0);
            return float4(blend, 1.0);
            
        case 6: // Height values
            return float4(material.height.xxx, 1.0);
            
        case 7: // Sharp features
            float sharp = ((int)(input.materialData0.w >> 4) & 0x01);
            return float4(sharp, sharp, 0, 1.0);
            
        default:
            return float4(1, 0, 1, 1); // Magenta for undefined
    }
}
#endif
```

## Conclusion

This shader system provides a complete solution for rendering dual contouring output with:

1. **Flexible Material System**: Support for up to 4 materials per vertex with smooth blending
2. **High Visual Quality**: Triplanar mapping, detail textures, and procedural enhancements
3. **Performance Scalability**: LOD support and optimized sampling strategies
4. **Advanced Effects**: Weather, subsurface scattering, and artistic controls
5. **Engine Integration**: Full compatibility with Unity's rendering features

The modular architecture allows for easy extension and customization while maintaining optimal performance for real-time applications.
