#pragma once

void ApplyNormalStrength(inout half3 norm) {
  norm = half3(norm.rg * _NormalStrength,
               lerp(1, norm.b, saturate(_NormalStrength)));
}

half3 unpack_normal(half2 xy) {
  // return half3(xy * 2 - 1, sqrt(1 - saturate(dot(xy, xy))));
  return half3((xy * 2 - 1) * _NormalStrength, 1);
}
