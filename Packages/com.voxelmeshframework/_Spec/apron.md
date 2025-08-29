**Apron** (also called a **halo** or **ghost cells**) = a 1-voxel thick padding layer added **around** each chunk.
If your interior chunk is `N×N×N`, you actually allocate `(N+2)×(N+2)×(N+2)` so you have one extra voxel on **every face**: indices `0` and `N+1` along each axis are the apron; `1..N` is the real chunk.

Why it exists (and why we use it):

* **Crack-free meshing:** Surface Nets needs the **8 corner samples** of each cell. At a chunk edge, some corners would be in the neighbor chunk—apron duplicates those values locally so a chunk can mesh by itself without seams.
* **Consistent gradients:** SDF-based normals use finite differences; apron lets you sample “just outside” the interior.
* **No cross-chunk lookups during meshing:** keeps jobs embarrassingly parallel (nice for Rayon).

How we fill it:

* Each chunk’s apron copies the **neighbor’s interior edge** (or, at world bounds, a default SDF value representing empty space).
* We treat apron cells as **read-only**; the **interior** `1..N` is authoritative.
* When edits cross a boundary, we mark **both** chunks dirty so each recomputes its apron before meshing.

Small mental picture (1D slice):

```
[ apron ][        interior (1..N)        ][ apron ]
   idx 0   1  2  ...       N-1   N          N+1
```

Sizing & overhead:

* Storage per chunk becomes `(N+2)^3` voxels instead of `N^3`.
* Overhead examples:

  * `N=16`  → **\~42.38%** extra
  * `N=32`  → **\~19.95%** extra
  * `N=64`  → **\~9.67%** extra
* For slabs (e.g., `64×64×8` → apron `66×66×10`), overhead is higher (**\~32.93%**) because the thin axis is relatively dominated by padding.

Practical rules we’ll follow in this system:

* Arrays are sized with apron; interior indices are `1..N` per axis.
* Authoring and edits write to interiors; **aprons are refreshed** from neighbors (or recomputed) before meshing.
* Meshing reads freely, including apron, so **no neighbor reads** or special-case borders—hence **no cracks**.
