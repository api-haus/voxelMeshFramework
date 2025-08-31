#pragma once

#include "./Normals.hlsl"

struct SingleUVTextureArraySampler {
  half3 blend;
  half2 uv;
  half2 uvDx;
  half2 uvDy;

  half4 sample(in int chan, TEXTURE2D_ARRAY(t2d), SAMPLER(smp)) {
    return SAMPLE_TEXTURE2D_ARRAY_GRAD(t2d, smp, uv, chan, uvDx, uvDy);
  }

  half sampleHeightLOD(in int chan, in int lod, TEXTURE2D_ARRAY(t2d),
                       SAMPLER(smp)) {
    return SAMPLE_TEXTURE2D_ARRAY_LOD(t2d, smp, uv, chan, lod).a;
  }

  void sampleNormal(in float3 worldNormal, in int chan, TEXTURE2D_ARRAY(t2d),
                    SAMPLER(smp), out half3 normal, out half smoothness,
                    out half ao) {
    half4 sample =
        SAMPLE_TEXTURE2D_ARRAY_GRAD(t2d, smp, uv, chan, uvDx, uvDy).agrb;

    // Unpack the tangent space normal
    half3 tangentNormal = unpack_normal(sample.xy);

    // Create a proper tangent space to world space transformation
    // Since we're using blended UVs, we approximate the tangent space using the
    // blend weights
    half3 absNormal = abs(worldNormal);

    // Determine the dominant axis and create tangent vectors accordingly
    half3 tangent, bitangent;
    if (absNormal.x >= absNormal.y && absNormal.x >= absNormal.z) {
      // X is dominant - YZ plane projection
      tangent = normalize(half3(0, sign(worldNormal.x), 0));
      bitangent = normalize(half3(0, 0, -sign(worldNormal.x)));
    } else if (absNormal.y >= absNormal.z) {
      // Y is dominant - XZ plane projection
      tangent = normalize(half3(-sign(worldNormal.y), 0, 0));
      bitangent = normalize(half3(0, 0, sign(worldNormal.y)));
    } else {
      // Z is dominant - XY plane projection
      tangent = normalize(half3(sign(worldNormal.z), 0, 0));
      bitangent = normalize(half3(0, sign(worldNormal.z), 0));
    }

    // Transform tangent space normal to world space
    // Blend between the transformed normal and original world normal based on
    // normal strength
    half3 worldTangentNormal =
        normalize(tangent * tangentNormal.x + bitangent * tangentNormal.y +
                  worldNormal * tangentNormal.z);

    // Interpolate based on the blend weights for smoother transitions
    half dominantWeight = max(blend.x, max(blend.y, blend.z));
    normal = normalize(lerp(worldNormal, worldTangentNormal, dominantWeight));

    smoothness = saturate(sample.b);
    ao = saturate(sample.a);
  }

  void gather(in float3 localNormal, in float3 localPos) {
    // Calculate triplanar blend weights
    blend = normalize(pow(abs(localNormal), _BlendContrast / 8.0));
    blend /= dot(blend, 1.0f);

    // Calculate individual triplanar UVs with correct coordinate assignments
    half2 uvx = localPos.zy * _UVScale; // X-axis uses YZ plane
    half2 uvy = localPos.xz * _UVScale; // Y-axis uses XZ plane
    half2 uvz = localPos.xy * _UVScale; // Z-axis uses XY plane

    // Handle negative normals to prevent mirroring artifacts
    if (localNormal.x < 0) {
      uvx.x = -uvx.x;
    }
    if (localNormal.y < 0) {
      uvy.x = -uvy.x;
    }
    if (localNormal.z >= 0) {
      uvz.x = -uvz.x;
    }

    // Add offsets to prevent seams
    uvx.y += 0.5;
    uvz.x += 0.5;

    // Lerp UVs based on blend weights to create single UV
    uv = uvx * blend.x + uvy * blend.y + uvz * blend.z;

    // Calculate derivatives for the lerped UV
    half2 uvxDx = ddx(uvx);
    half2 uvyDx = ddx(uvy);
    half2 uvzDx = ddx(uvz);

    half2 uvxDy = ddy(uvx);
    half2 uvyDy = ddy(uvy);
    half2 uvzDy = ddy(uvz);

    uvDx = uvxDx * blend.x + uvyDx * blend.y + uvzDx * blend.z;
    uvDy = uvxDy * blend.x + uvyDy * blend.y + uvzDy * blend.z;
  }

  void gatherLOD(in float3 localNormal, in float3 localPos) {
    // Calculate triplanar blend weights
    blend = normalize(pow(abs(localNormal), _BlendContrast / 8.0));
    blend /= dot(blend, 1.0f);

    // Calculate individual triplanar UVs with correct coordinate assignments
    half2 uvx = localPos.zy * _UVScale; // X-axis uses YZ plane
    half2 uvy = localPos.xz * _UVScale; // Y-axis uses XZ plane
    half2 uvz = localPos.xy * _UVScale; // Z-axis uses XY plane

    // Handle negative normals to prevent mirroring artifacts
    if (localNormal.x < 0) {
      uvx.x = -uvx.x;
    }
    if (localNormal.y < 0) {
      uvy.x = -uvy.x;
    }
    if (localNormal.z >= 0) {
      uvz.x = -uvz.x;
    }

    // Add offsets to prevent seams
    uvx.y += 0.5;
    uvz.x += 0.5;

    // Lerp UVs based on blend weights to create single UV
    uv = uvx * blend.x + uvy * blend.y + uvz * blend.z;
  }

  void offset(in float2 offset) { uv += offset; }
};
