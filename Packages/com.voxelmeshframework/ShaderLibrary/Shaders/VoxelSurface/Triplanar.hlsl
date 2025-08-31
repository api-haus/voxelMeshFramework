#pragma once

// #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

#include "./Normals.hlsl"

struct TriplanarTextureArraySampler {
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
    half cx = SAMPLE_TEXTURE2D_ARRAY_LOD(t2d, smp, uvx, chan, lod).a * blend.x;
    half cy = SAMPLE_TEXTURE2D_ARRAY_LOD(t2d, smp, uvy, chan, lod).a * blend.y;
    half cz = SAMPLE_TEXTURE2D_ARRAY_LOD(t2d, smp, uvz, chan, lod).a * blend.z;
    return (cx + cy + cz);
  }

  // reference:
  // https://bgolus.medium.com/normal-mapping-for-a-triplanar-shader-10bf39dca05a
  void sampleNormal(in float3 worldNormal, in int chan, TEXTURE2D_ARRAY(t2d),
                    SAMPLER(smp), out half3 normal, out half smoothness,
                    out half ao) {
    half4 cx =
        SAMPLE_TEXTURE2D_ARRAY_GRAD(t2d, smp, uvx, chan, uvxDx, uvxDy).agrb;
    half4 cy =
        SAMPLE_TEXTURE2D_ARRAY_GRAD(t2d, smp, uvy, chan, uvyDx, uvyDy).agrb;
    half4 cz =
        SAMPLE_TEXTURE2D_ARRAY_GRAD(t2d, smp, uvz, chan, uvzDx, uvzDy).agrb;

    // Tangent space normal maps
    half3 tnormalX = unpack_normal(cx.xy);
    half3 tnormalY = unpack_normal(cy.xy);
    half3 tnormalZ = unpack_normal(cz.xy);

    // Get the sign (-1 or 1) of the surface normal
    half3 axisSign = sign(worldNormal);
    // Flip tangent normal z to account for surface normal facing
    tnormalX.z *= axisSign.x;
    tnormalY.z *= axisSign.y;
    tnormalZ.z *= axisSign.z;
    // Swizzle tangent normals to match world orientation and triblend
    normal = normalize(tnormalX.zyx * blend.x + tnormalY.xzy * blend.y +
                       tnormalZ.xyz * blend.z);

    smoothness = saturate( //
        cx.b * blend.x +   //
        cy.b * blend.y +   //
        cz.b * blend.z);

    ao = saturate(       //
        cx.a * blend.x + //
        cy.a * blend.y + //
        cz.a * blend.z);
  }

  void gather(in float3 localNormal, in float3 localPos) {
    blend = normalize(pow(abs(localNormal), _BlendContrast / 8.0));
    blend /= dot(blend, 1.0f);

    uvx = localPos.yz * _UVScale;
    uvy = localPos.zx * _UVScale;
    uvz = localPos.xy * _UVScale;

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

  void gatherLOD(in float3 localNormal, in float3 localPos) {
    blend = normalize(pow(abs(localNormal), _BlendContrast / 8.0));
    blend /= dot(blend, 1.0f);

    uvx = localPos.yz * _UVScale;
    uvy = localPos.zx * _UVScale;
    uvz = localPos.xy * _UVScale;
  }
};
