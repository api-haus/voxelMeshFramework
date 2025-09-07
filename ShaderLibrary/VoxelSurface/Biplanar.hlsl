#pragma once

// #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

#include "./Normals.hlsl"

struct BiplanarTextureArraySampler {
  // Simplified struct - no pre-cached blend weights or UVs
  float3 worldPos;
  float3 worldNormal;

  half2 uvOffset;

  half4 sample(in int chan, TEXTURE2D_ARRAY(t2d), SAMPLER(smp)) {
    // Coordinate derivatives for texturing
    float3 p = worldPos;
    float3 n = abs(worldNormal);
    float3 dpdx = ddx(p);
    float3 dpdy = ddy(p);

    // Determine major axis (in x; yz are following axis)
    int3 ma = (n.x > n.y && n.x > n.z) ? int3(0, 1, 2)
              : (n.y > n.z)            ? int3(1, 2, 0)
                                       : int3(2, 0, 1);

    // Determine minor axis (in x; yz are following axis)
    int3 mi = (n.x < n.y && n.x < n.z) ? int3(0, 1, 2)
              : (n.y < n.z)            ? int3(1, 2, 0)
                                       : int3(2, 0, 1);

    // Determine median axis (in x; yz are following axis)
    int3 me = int3(3, 3, 3) - mi - ma;

    // Project + fetch
    half2 uva = half2(p[ma.y], p[ma.z]) * _UVScale;
    half2 uvb = half2(p[me.y], p[me.z]) * _UVScale;
    half2 uvaDx = half2(dpdx[ma.y], dpdx[ma.z]) * _UVScale;
    half2 uvbDx = half2(dpdx[me.y], dpdx[me.z]) * _UVScale;
    half2 uvaDy = half2(dpdy[ma.y], dpdy[ma.z]) * _UVScale;
    half2 uvbDy = half2(dpdy[me.y], dpdy[me.z]) * _UVScale;

    half4 x = SAMPLE_TEXTURE2D_ARRAY_GRAD(t2d, smp, uva + uvOffset, chan, uvaDx,
                                          uvaDy);
    half4 y = SAMPLE_TEXTURE2D_ARRAY_GRAD(t2d, smp, uvb + uvOffset, chan, uvbDx,
                                          uvbDy);

    // Blend factors
    half2 w = half2(n[ma.x], n[me.x]);
    // Make local support
    const half k = (half)0.5773502691896258; // 1/sqrt(3)
    w = saturate((w - k) / (1.0 - k));
    // Shape transition
    w = pow(w, _BlendContrast / 8.0);
    // Blend and return
    return (x * w.x + y * w.y) / (w.x + w.y);
  }

  half sampleHeightLOD(in int chan, in int lod, TEXTURE2D_ARRAY(t2d),
                       SAMPLER(smp)) {
    // Simplified calculation for LOD sampling (no derivatives needed)
    float3 p = worldPos;
    float3 n = abs(worldNormal);

    // Determine major axis (in x; yz are following axis)
    int3 ma = (n.x > n.y && n.x > n.z) ? int3(0, 1, 2)
              : (n.y > n.z)            ? int3(1, 2, 0)
                                       : int3(2, 0, 1);

    // Determine minor axis (in x; yz are following axis)
    int3 mi = (n.x < n.y && n.x < n.z) ? int3(0, 1, 2)
              : (n.y < n.z)            ? int3(1, 2, 0)
                                       : int3(2, 0, 1);

    // Determine median axis (in x; yz are following axis)
    int3 me = int3(3, 3, 3) - mi - ma;

    // Project + fetch
    half2 uva = half2(p[ma.y], p[ma.z]) * _UVScale;
    half2 uvb = half2(p[me.y], p[me.z]) * _UVScale;

    half x = SAMPLE_TEXTURE2D_ARRAY_LOD(t2d, smp, uva + uvOffset, chan, lod).a;
    half y = SAMPLE_TEXTURE2D_ARRAY_LOD(t2d, smp, uvb + uvOffset, chan, lod).a;

    // Blend factors
    half2 w = half2(n[ma.x], n[me.x]);
    // Make local support
    const half k = (half)0.5773502691896258; // 1/sqrt(3)
    w = saturate((w - k) / (1.0 - k));
    // Shape transition
    w = pow(w, _BlendContrast / 8.0);
    // Blend and return
    return (x * w.x + y * w.y) / (w.x + w.y);
  }

  // reference:
  // https://bgolus.medium.com/normal-mapping-for-a-triplanar-shader-10bf39dca05a
  void sampleNormal(in float3 worldNormal, in int chan, TEXTURE2D_ARRAY(t2d),
                    SAMPLER(smp), out half3 normal, out half smoothness,
                    out half ao) {
    // Coordinate derivatives for texturing
    float3 p = worldPos;
    float3 n = abs(worldNormal);
    float3 dpdx = ddx(p);
    float3 dpdy = ddy(p);

    // Determine major axis (in x; yz are following axis)
    int3 ma = (n.x > n.y && n.x > n.z) ? int3(0, 1, 2)
              : (n.y > n.z)            ? int3(1, 2, 0)
                                       : int3(2, 0, 1);

    // Determine minor axis (in x; yz are following axis)
    int3 mi = (n.x < n.y && n.x < n.z) ? int3(0, 1, 2)
              : (n.y < n.z)            ? int3(1, 2, 0)
                                       : int3(2, 0, 1);

    // Determine median axis (in x; yz are following axis)
    int3 me = int3(3, 3, 3) - mi - ma;

    // Project + fetch
    half2 uva = half2(p[ma.y], p[ma.z]) * _UVScale;
    half2 uvb = half2(p[me.y], p[me.z]) * _UVScale;
    half2 uvaDx = half2(dpdx[ma.y], dpdx[ma.z]) * _UVScale;
    half2 uvbDx = half2(dpdx[me.y], dpdx[me.z]) * _UVScale;
    half2 uvaDy = half2(dpdy[ma.y], dpdy[ma.z]) * _UVScale;
    half2 uvbDy = half2(dpdy[me.y], dpdy[me.z]) * _UVScale;

    half4 x = SAMPLE_TEXTURE2D_ARRAY_GRAD(t2d, smp, uva + uvOffset, chan, uvaDx,
                                          uvaDy)
                  .agrb;
    half4 y = SAMPLE_TEXTURE2D_ARRAY_GRAD(t2d, smp, uvb + uvOffset, chan, uvbDx,
                                          uvbDy)
                  .agrb;

    half3 tA = unpack_normal(x.xy);
    half3 tB = unpack_normal(y.xy);

    // Build GPU Gems 3 style world-space contributions per projection
    // without dynamic component assignments
    half3 eUma = half3(ma.y == 0 ? 1.0h : 0.0h, ma.y == 1 ? 1.0h : 0.0h,
                       ma.y == 2 ? 1.0h : 0.0h);
    half3 eVma = half3(ma.z == 0 ? 1.0h : 0.0h, ma.z == 1 ? 1.0h : 0.0h,
                       ma.z == 2 ? 1.0h : 0.0h);
    half3 eUme = half3(me.y == 0 ? 1.0h : 0.0h, me.y == 1 ? 1.0h : 0.0h,
                       me.y == 2 ? 1.0h : 0.0h);
    half3 eVme = half3(me.z == 0 ? 1.0h : 0.0h, me.z == 1 ? 1.0h : 0.0h,
                       me.z == 2 ? 1.0h : 0.0h);

    // For Z-normal planes, map [x,y] -> [U,V]; for X/Y-normal planes, swap to
    // [y,x]
    half2 mA = (ma.x == 2) ? half2(tA.x, tA.y) : half2(tA.y, tA.x);
    half2 mB = (me.x == 2) ? half2(tB.x, tB.y) : half2(tB.y, tB.x);

    half3 n1 = eUma * mA.x + eVma * mA.y;
    half3 n2 = eUme * mB.x + eVme * mB.y;

    // Blend factors
    half2 w = half2(n[ma.x], n[me.x]);
    // Make local support
    const half k = (half)0.5773502691896258; // 1/sqrt(3)
    w = saturate((w - k) / (1.0 - k));
    // Shape transition
    w = pow(w, _BlendContrast / 8.0);

    // Normalize weights
    half denom = max(w.x + w.y, 1e-5h);
    w /= denom;

    // GPU Gems 3: blend contributions, add world normal, then normalize
    normal = normalize(n1 * w.x + n2 * w.y + worldNormal);
    smoothness = saturate(x.b * w.x + y.b * w.y);
    ao = saturate(x.a * w.x + y.a * w.y);
  }

  void gatherLOD(in float3 localNormal, in float3 localPos) {
    // Store world position and normal for use in sampling functions
    worldPos = localPos;
    worldNormal = localNormal;
  }

  void gather(in float3 localNormal, in float3 localPos) {
    // Store world position and normal for use in sampling functions
    worldPos = localPos;
    worldNormal = localNormal;
  }

  void offset(in float2 offset) { uvOffset = offset; }
};
