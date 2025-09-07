#pragma once

#if defined(_TEX_SAMPLE_ONE)
#include "./SingleUV.hlsl"
#define kSampler SingleUVTextureArraySampler
#elif defined(_TEX_SAMPLE_TWO)
#include "./Biplanar.hlsl"
#define kSampler BiplanarTextureArraySampler
#elif defined(_TEX_SAMPLE_THREE)
#include "./Triplanar.hlsl"
#define kSampler TriplanarTextureArraySampler
#else
// Default to triplanar if no sampling mode is defined
#include "./Triplanar.hlsl"
#define kSampler TriplanarTextureArraySampler
#endif

#include "./Parallax.hlsl"

// Debug function to visualize material weights
void voxel_surface_debug_weights(in half4 materialWeights, out half3 oAlbedo,
                                 out half oMetallic, out half oSmoothness,
                                 out half3 oEmission, out half oOcclusion,
                                 out half3 oNormal) {
  // Visualize weights as colors for debugging
  oAlbedo = half3(materialWeights.r, materialWeights.g, materialWeights.b);
  oMetallic = 0;
  oSmoothness = 0.5;
  oEmission = half3(0, 0, 0);
  oOcclusion = 1;
  oNormal = half3(0, 0, 1);
}

void voxel_surface_half(in half4 materialWeights, in half3 worldSpacePosition,
                        in half3 worldSpaceNormal, in half3 viewDir,
                        out half3 oAlbedo, out half oMetallic,
                        out half oSmoothness, out half3 oEmission,
                        out half oOcclusion, out half3 oNormal) {
  kSampler s = (kSampler)0;
#if _RP_HDRP
  s.gather(worldSpaceNormal, GetAbsolutePositionWS(worldSpacePosition));
#else
  s.gather(worldSpaceNormal, worldSpacePosition);
#endif

  // Initialize blended properties
  oAlbedo = half3(0, 0, 0);
  oMetallic = 0;
  oSmoothness = 0;
  oEmission = half3(0, 0, 0);
  oOcclusion = 0;
  oNormal = half3(0, 0, 1);

  // Normalize weights (should already be normalized, but ensure safety)
  half4 weights = materialWeights;
  half totalWeight = weights.r + weights.g + weights.b + weights.a;
  if (totalWeight > 0.01) {
    weights /= totalWeight;
  } else {
    // Fallback: use material 0 with full weight
    weights = half4(1, 0, 0, 0);
  }

  // Apply material contrast to control blending sharpness
  // Higher contrast = sharper boundaries, Lower contrast = softer blends
  weights = pow(weights, _MaterialContrast);

  // Re-normalize after contrast adjustment
  totalWeight = weights.r + weights.g + weights.b + weights.a;
  if (totalWeight > 0.01) {
    weights /= totalWeight;
  }

  // Sample and blend up to 4 materials
  UNITY_UNROLL
  for (int matIdx = 0; matIdx < 4; matIdx++) {
    half weight = weights[matIdx];

    if (weight > 0.01) {
      // Skip materials with negligible influence
      // Sample material textures
      half4 albedoHeight = s.sample(matIdx, _Diffuse, sampler_Diffuse);
      float materialHeight = albedoHeight.a;

#if _USE_PARALLAX
      // Apply parallax offset
      half2 offset = ParallaxOffsetT(materialHeight, _Parallax, viewDir);
      s.offset(offset);

      // Re-sample with parallax offset
      albedoHeight = s.sample(matIdx, _Diffuse, sampler_Diffuse);
      materialHeight = albedoHeight.a;
#endif

      // Sample additional properties
      half3 materialNormal;
      half materialSmoothness;
      half materialAO;

      s.sampleNormal(worldSpaceNormal, matIdx, _NormalSAO, sampler_NormalSAO,
                     materialNormal, materialSmoothness, materialAO);

      half4 emissiveMetal =
          s.sample(matIdx, _EmissiveMetallic, sampler_Diffuse);

      // Accumulate weighted properties (use original weight for now)
      oAlbedo += albedoHeight.rgb * weight;
      oMetallic += emissiveMetal.a * weight;
      oSmoothness += materialSmoothness * weight;
      oEmission += emissiveMetal.rgb * _EmissiveStrength * weight;
      oOcclusion += materialAO * weight;
      oNormal += materialNormal * weight;
    }
  }

  // Normalize accumulated normal
  oNormal = normalize(oNormal);

#if _USE_FRESNEL
  oAlbedo +=
      oAlbedo *
      pow((1.0 - saturate(dot(worldSpaceNormal, viewDir))), _FresnelPower) *
      _FresnelStrength;
#endif

#if _USE_DEBUG_WEIGHTS
  voxel_surface_debug_weights(materialWeights, oAlbedo, oMetallic, oSmoothness,
                              oEmission, oOcclusion, oNormal);
#endif
}
