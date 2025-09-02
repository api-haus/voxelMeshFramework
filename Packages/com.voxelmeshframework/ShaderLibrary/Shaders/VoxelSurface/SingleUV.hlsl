#pragma once

#include "./Normals.hlsl"

inline half3 blend_triplanar_normal(in half3 mappedNormal,
                                    in half3 surfaceNormal) {
  half3 n;
  n.xy = mappedNormal.xy + surfaceNormal.xy;
  n.z = mappedNormal.z * surfaceNormal.z;
  return n;
}

struct TriplanarUV {
  half2 x, y, z;
};

TriplanarUV get_triplanar_uv(in float3 localPos, in float3 localNormal) {
  TriplanarUV triUV;
  float3 p = localPos * _UVScale;
  
  // Calculate UV coordinates for each axis projection
  triUV.x = p.zy; // X-axis uses YZ plane
  triUV.y = p.xz; // Y-axis uses XZ plane  
  triUV.z = p.xy; // Z-axis uses XY plane
  
  // Handle negative normals to prevent mirroring artifacts
  if (localNormal.x < 0) {
    triUV.x.x = -triUV.x.x;
  }
  if (localNormal.y < 0) {
    triUV.y.x = -triUV.y.x;
  }
  if (localNormal.z >= 0) {
    triUV.z.x = -triUV.z.x;
  }
  
  // Add offsets to prevent seams
  triUV.x.y += 0.5;
  triUV.z.x += 0.5;
  
  return triUV;
}

half3 get_triplanar_weights(in float3 localNormal, in half heightX, in half heightY, in half heightZ) {
  half3 triW = abs(localNormal);
  triW = saturate(triW - _BlendOffset);
  // Height-based blending will be implemented in next step
  triW = pow(triW, _BlendContrast / 8.0);
  return triW / (triW.x + triW.y + triW.z);
}

half3 get_triplanar_weights_simple(in float3 localNormal) {
  half3 triW = abs(localNormal);
  triW = pow(triW, _BlendContrast / 8.0);
  return triW / (triW.x + triW.y + triW.z);
}

struct TriplanarTextureArraySampler {
  TriplanarUV triUV;
  half3 triWeights;
  half2 uvDxX, uvDyX;
  half2 uvDxY, uvDyY;
  half2 uvDxZ, uvDyZ;

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
    half4 s = SAMPLE_TEXTURE2D_ARRAY_GRAD(t2d, smp, uv, chan, uvDx, uvDy).agrb;

    // Unpack tangent-space normal once (single sample)
    half3 tnorm = unpack_normal(s.xy);

    // Create axis-oriented interpretations and correct handedness per axis
    half3 nx = tnorm;
    half3 ny = tnorm;
    half3 nz = tnorm;
    if (worldNormal.x < 0) {
      nx.x = -nx.x;
    }
    if (worldNormal.y < 0) {
      ny.x = -ny.x;
    }
    if (worldNormal.z >= 0) {
      nz.x = -nz.x;
    }

    // Convert to world-space per axis using triplanar swizzles and blend
    half3 wx = blend_triplanar_normal(nx, worldNormal.zyx).zyx;
    half3 wy = blend_triplanar_normal(ny, worldNormal.xzy).xzy;
    half3 wz = blend_triplanar_normal(nz, worldNormal);
    normal = normalize(wx * blend.x + wy * blend.y + wz * blend.z);

    smoothness = saturate(s.b);
    ao = saturate(s.a);
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
