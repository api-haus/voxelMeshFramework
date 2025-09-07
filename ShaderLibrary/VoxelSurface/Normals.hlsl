#pragma once

void ApplyNormalStrength(inout half3 norm) {
  norm = half3(norm.rg * _NormalStrength,
               lerp(1, norm.b, saturate(_NormalStrength)));
}

half3 unpack_normal(half2 xy) {
  half2 n = xy * 2.0h - 1.0h;
  n *= _NormalStrength;
  half nz = sqrt(saturate(1.0h - dot(n, n)));
  return half3(n, nz);
}
