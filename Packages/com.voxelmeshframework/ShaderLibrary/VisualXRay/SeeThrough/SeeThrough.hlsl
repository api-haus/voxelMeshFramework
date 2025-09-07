#pragma once

void see_through_half(in half3 pos, in half3 norm, out half alpha,
                      out half alpha_clip) {
  alpha = 1;
  alpha_clip = .5;
}
