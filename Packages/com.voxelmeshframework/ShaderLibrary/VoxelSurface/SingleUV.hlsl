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
  triUV.x = p.yz; // X-axis uses YZ plane (match Triplanar.hlsl)
  triUV.y = p.zx; // Y-axis uses ZX plane (match Triplanar.hlsl)
  triUV.z = p.xy; // Z-axis uses XY plane (match Triplanar.hlsl)

  return triUV;
}

half3 get_triplanar_weights(in float3 localNormal, in half heightX,
                            in half heightY, in half heightZ) {
  half3 triW = abs(localNormal);
  triW = saturate(triW);
  // Height-based blending will be implemented in next step
  triW = pow(triW, _BlendContrast / 8.0);
  return triW / (triW.x + triW.y + triW.z);
}

half3 get_triplanar_weights_simple(in float3 localNormal) {
  half3 triW = abs(localNormal);
  triW = pow(triW, _BlendContrast / 8.0);
  return triW / (triW.x + triW.y + triW.z);
}

// Object-space interleaved gradient noise for stable dithering
inline float interleaved_gradient_noise(in float2 p) {
  return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715))));
}

inline uint select_axis_from_weights(in half3 w, in float threshold) {
  half xy = w.x + w.y;
  return (threshold < w.x) ? 0u : ((threshold < xy) ? 1u : 2u);
}

struct SingleUVTextureArraySampler {
  TriplanarUV triUV;
  half3 triWeights;
  half2 uvDxX, uvDyX;
  half2 uvDxY, uvDyY;
  half2 uvDxZ, uvDyZ;
  half2 uv;
  half2 uvDx, uvDy;

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
    normal =
        normalize(wx * triWeights.x + wy * triWeights.y + wz * triWeights.z);

    smoothness = saturate(s.b);
    ao = saturate(s.a);
  }

  void gather(in float3 localNormal, in float3 localPos) {
    triWeights = get_triplanar_weights_simple(localNormal);
    triUV = get_triplanar_uv(localPos, localNormal);

    uv = triUV.x * triWeights.x + triUV.y * triWeights.y +
         triUV.z * triWeights.z;

    half2 uvxDx = ddx(triUV.x);
    half2 uvyDx = ddx(triUV.y);
    half2 uvzDx = ddx(triUV.z);

    half2 uvxDy = ddy(triUV.x);
    half2 uvyDy = ddy(triUV.y);
    half2 uvzDy = ddy(triUV.z);

    uvDx = uvxDx * triWeights.x + uvyDx * triWeights.y + uvzDx * triWeights.z;
    uvDy = uvxDy * triWeights.x + uvyDy * triWeights.y + uvzDy * triWeights.z;
  }

  void gatherLOD(in float3 localNormal, in float3 localPos) {
    triWeights = get_triplanar_weights_simple(localNormal);
    triUV = get_triplanar_uv(localPos, localNormal);
    uv = triUV.x * triWeights.x + triUV.y * triWeights.y +
         triUV.z * triWeights.z;
  }

  void offset(in float2 offset) { uv += offset; }

  // Dithered single-axis selection to reduce cross-fade blur with one sample
  void gatherDithered(in float3 localNormal, in float3 localPos) {
    triWeights = get_triplanar_weights_simple(localNormal);
    triUV = get_triplanar_uv(localPos, localNormal);

    float t = interleaved_gradient_noise(localPos.xy);
    uint axis = select_axis_from_weights(triWeights, t);

    // Derivatives for each axis
    half2 ddxX = ddx(triUV.x);
    half2 ddyX = ddy(triUV.x);
    half2 ddxY = ddx(triUV.y);
    half2 ddyY = ddy(triUV.y);
    half2 ddxZ = ddx(triUV.z);
    half2 ddyZ = ddy(triUV.z);

    if (axis == 0u) {
      uv = triUV.x;
      uvDx = ddxX;
      uvDy = ddyX;
    } else if (axis == 1u) {
      uv = triUV.y;
      uvDx = ddxY;
      uvDy = ddyY;
    } else {
      uv = triUV.z;
      uvDx = ddxZ;
      uvDy = ddyZ;
    }
  }

  void gatherLODDithered(in float3 localNormal, in float3 localPos) {
    triWeights = get_triplanar_weights_simple(localNormal);
    triUV = get_triplanar_uv(localPos, localNormal);

    float t = interleaved_gradient_noise(localPos.xy);
    uint axis = select_axis_from_weights(triWeights, t);

    if (axis == 0u) {
      uv = triUV.x;
    } else if (axis == 1u) {
      uv = triUV.y;
    } else {
      uv = triUV.z;
    }
  }
};
