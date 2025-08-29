#pragma once

// #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

#include "./Normals.hlsl"

struct BiplanarTextureArraySampler {
  half3 blend;

  half2 uvx;
  half2 uvy;
  half2 uvz;

  half2 uvxDx;
  half2 uvyDx;
  half2 uvzDx;

  half2 uvxDy;
  half2 uvyDy;
  half2 uvzDy;

  half4 sample(in int chan, TEXTURE2D_ARRAY(t2d), SAMPLER(smp)) {
    half4 cx = SAMPLE_TEXTURE2D_ARRAY_GRAD(t2d, smp, uvx, chan, uvxDx, uvxDy) *
               blend.x;
    half4 cy = SAMPLE_TEXTURE2D_ARRAY_GRAD(t2d, smp, uvy, chan, uvyDx, uvyDy) *
               blend.y;
    half4 cz = SAMPLE_TEXTURE2D_ARRAY_GRAD(t2d, smp, uvz, chan, uvzDx, uvzDy) *
               blend.z;
    return (cx + cy + cz);
  }

  half sampleHeightLOD(in int chan, in int lod, TEXTURE2D_ARRAY(t2d),
                       SAMPLER(smp)) {
    // half cx = SAMPLE_TEXTURE2D_ARRAY_LOD(t2d, smp, uvx, chan, lod).a *
    // blend.x; half cy = SAMPLE_TEXTURE2D_ARRAY_LOD(t2d, smp, uvy, chan, lod).a
    // * blend.y; half cz = SAMPLE_TEXTURE2D_ARRAY_LOD(t2d, smp, uvz, chan,
    // lod).a * blend.z; return (cx + cy + cz);
    return 0;
  }

  // reference:
  // https://bgolus.medium.com/normal-mapping-for-a-triplanar-shader-10bf39dca05a
  void sampleNormal(in float3 worldNormal, in int chan, TEXTURE2D_ARRAY(t2d),
                    SAMPLER(smp), out half3 normal, out half smoothness,
                    out half ao) {
    normal = worldNormal;
    smoothness = 0;
    ao = 1;
  }

  void gatherLOD(in float3 localNormal, in float3 localPos) {
    // Coordinate derivatives for texturing
    float3 p = wpos;
    float3 n = abs(wnrm);
    float3 dpdx = ddx(p);
    float3 dpdy = ddy(p);
  }

  void gather(in float3 localNormal, in float3 localPos) {
    gatherLOD(localNormal, localPos);

    uvxDx = ddx(uvx);
    uvyDx = ddx(uvy);
    uvzDx = ddx(uvz);

    uvxDy = ddy(uvx);
    uvyDy = ddy(uvy);
    uvzDy = ddy(uvz);
  }

  void offset(in float2 offset) {
    uvx += offset;
    uvy += offset;
    uvz += offset;
  }
};
