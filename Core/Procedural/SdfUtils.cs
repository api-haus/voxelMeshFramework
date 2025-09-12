// ReSharper disable InconsistentNaming

namespace Voxels.Core.Procedural
{
	using Unity.Mathematics;
	using static Unity.Mathematics.math;

	/// <summary>
	///   Heavily inspired by Inigo Quilez on 3D SDF.
	/// </summary>
	public static class sdf
	{
		/// <summary>
		///   Hard union of two SDFs (inside-negative convention). Equivalent to <c>min(d1, d2)</c>.
		/// </summary>
		/// <param name="d1">Signed distance of the first shape (inside-positive).</param>
		/// <param name="d2">Signed distance of the second shape (inside-positive).</param>
		/// <returns>Inside-positive signed distance of the union.</returns>
		public static float opUnion(float d1, float d2)
		{
			return max(d1, d2);
		}

		/// <summary>
		///   Transform point <paramref name="p" /> by the inverse of transformation matrix <paramref name="t" />.
		///   Used to transform points from world space to the local space of an SDF shape.
		/// </summary>
		/// <param name="t">Transformation matrix (typically from a Transform component).</param>
		/// <param name="p">Point to transform.</param>
		/// <returns>Point transformed to local space of the shape.</returns>
		public static float3 opTx(float4x4 t, float3 p)
		{
			return transform(inverse(t), p);
		}

		/// <summary>
		///   Hard subtraction (difference) of two SDFs (inside-negative convention). Keeps <paramref name="d1" /> and removes
		///   <paramref name="d2" />.
		/// </summary>
		/// <param name="d1">Signed distance of the base shape (inside-positive).</param>
		/// <param name="d2">Signed distance of the subtracting shape (inside-positive).</param>
		/// <returns>Inside-positive signed distance of the difference <c>d1 \\ d2</c>.</returns>
		public static float opSubtraction(float d1, float d2)
		{
			return min(d1, -d2);
		}

		/// <summary>
		///   Hard intersection of two SDFs (inside-negative convention). Equivalent to <c>max(d1, d2)</c>.
		/// </summary>
		/// <param name="d1">Signed distance of the first shape (inside-positive).</param>
		/// <param name="d2">Signed distance of the second shape (inside-positive).</param>
		/// <returns>Inside-positive signed distance of the intersection.</returns>
		public static float opIntersection(float d1, float d2)
		{
			return min(d1, d2);
		}

		/// <summary>
		///   Symmetric difference (XOR) of two SDFs (inside-negative convention): interior where exactly one shape is inside.
		/// </summary>
		/// <param name="d1">Signed distance of the first shape (inside-positive).</param>
		/// <param name="d2">Signed distance of the second shape (inside-positive).</param>
		/// <returns>Inside-positive signed distance of the symmetric difference.</returns>
		public static float opXor(float d1, float d2)
		{
			return opIntersection(opUnion(d1, d2), -opIntersection(d1, d2));
		}

		/// <summary>
		///   Smooth union of two SDFs (inside-negative convention). Blends surfaces within a region controlled by
		///   <paramref name="k" />.
		/// </summary>
		/// <param name="d1">Signed distance of the first shape (inside-positive).</param>
		/// <param name="d2">Signed distance of the second shape (inside-positive).</param>
		/// <param name="k">Blend width scale (larger values increase smoothing span).</param>
		/// <returns>Inside-positive signed distance of the smoothed union.</returns>
		public static float opSmoothUnion(float d1, float d2, float k)
		{
			k *= 4.0f;
			var h = min(k - abs(d1 - d2), 0.0f);
			return max(d1, d2) - (h * h * 0.25f / k);
		}

		/// <summary>
		///   Smooth subtraction (difference) of two SDFs (inside-negative convention).
		///   Equivalent to <c>-SmoothUnion(d1, -d2, k)</c>.
		/// </summary>
		/// <param name="d1">Signed distance of the base shape (inside-positive).</param>
		/// <param name="d2">Signed distance of the subtracting shape (inside-positive).</param>
		/// <param name="k">Blend width scale (larger values increase smoothing span).</param>
		/// <returns>Inside-positive signed distance of the smoothed subtraction.</returns>
		public static float opSmoothSubtraction(float d1, float d2, float k)
		{
			return -opSmoothUnion(d1, -d2, k);
		}

		/// <summary>
		///   Smooth intersection of two SDFs (inside-negative convention).
		///   Equivalent to <c>-SmoothUnion(-d1, -d2, k)</c>.
		/// </summary>
		/// <param name="d1">Signed distance of the first shape (inside-positive).</param>
		/// <param name="d2">Signed distance of the second shape (inside-positive).</param>
		/// <param name="k">Blend width scale (larger values increase smoothing span).</param>
		/// <returns>Inside-positive signed distance of the smoothed intersection.</returns>
		public static float opSmoothIntersection(float d1, float d2, float k)
		{
			return -opSmoothUnion(-d1, -d2, k);

			//k *= 4.0;
			// float h = max(k-abs(d1-d2),0.0);
			// return max(d1, d2) + h*h*0.25/k;
		}

		/// <summary>
		///   Signed distance for a 3D 5-pointed star prism centered at the origin.
		///   Star lies in the XY plane; the prism is extruded along Z with thickness following 2D inside depth.
		///   Local half-thickness grows from 0 at the 2D boundary toward the center and is clamped by
		///   <paramref name="halfHeight" />.
		///   Returns inside-positive distance (matches project SDF convention).
		/// </summary>
		/// <param name="p">Point to evaluate, in the same space as the SDF. XY defines the star shape; Z is the extrusion axis.</param>
		/// <param name="r">Outer radius of the star tips measured from the center in XZ.</param>
		/// <param name="rf">Inner radius factor (0..1) controlling point indentation; lower = sharper points.</param>
		/// <param name="halfHeight">Half the prism height along Y (full height is 2 × <paramref name="halfHeight" />).</param>
		/// <returns>Inside-positive signed distance to the star prism surface.</returns>
		public static float sdStar5Prism(float3 p, float r, float rf, float halfHeight)
		{
			var d2 = sdStar5_2d(p.xy, r, rf); // outside-positive 2D star distance
			var insideDepth = max(0f, -d2); // how far inside the 2D star
			var hLocal = min(halfHeight, insideDepth); // local half-thickness
			var d = max(d2, abs(p.z) - hLocal); // outside-positive extrude with variable thickness
			return -d; // inside-positive
		}

		/// <summary>
		///   2D 5-pointed star SDF (outside-positive) used by <see cref="sdStar5Prism" />.
		///   Port of the common HLSL/GLSL function; operates on the XY projection.
		/// </summary>
		/// <param name="p">Point in the star's 2D plane (XY).</param>
		/// <param name="r">Outer radius of the star tips.</param>
		/// <param name="rf">Inner radius factor (0..1).</param>
		/// <returns>Outside-positive signed distance in 2D.</returns>
		static float sdStar5_2d(float2 p, float r, float rf)
		{
			var k1 = new float2(0.809016994375f, -0.587785252292f);
			var k2 = new float2(-k1.x, k1.y);
			p.x = abs(p.x);
			p -= 2f * max(dot(k1, p), 0f) * k1;
			p -= 2f * max(dot(k2, p), 0f) * k2;
			p.x = abs(p.x);
			p.y -= r;
			var ba = (rf * new float2(-k1.y, k1.x)) - new float2(0f, 1f);
			var h = clamp(dot(p, ba) / dot(ba, ba), 0f, r);
			return length(p - (ba * h)) * sign((p.y * ba.x) - (p.x * ba.y));
		}

		// ===== IQ Distance Function Primitives (inside-positive) =====
		/// <summary>
		///   Half-space (plane) SDF. <paramref name="n" /> must be normalized. Inside is the side opposite the normal.
		/// </summary>
		public static float sdPlane(float3 p, float3 n, float h)
		{
			return -(dot(p, n) + h);
		}

		/// <summary>
		///   Sphere SDF centered at origin with radius <paramref name="r" />.
		/// </summary>
		public static float sdSphere(float3 p, float r)
		{
			return r - length(p);
		}

		/// <summary>
		///   Axis-aligned box SDF with half-extents <paramref name="b" />.
		/// </summary>
		public static float sdBox(float3 p, float3 b)
		{
			var q = abs(p) - b;
			var outside = length(max(q, 0f)) + min(max(q.x, max(q.y, q.z)), 0f);
			return -outside;
		}

		/// <summary>
		///   Rounded axis-aligned box SDF with half-extents <paramref name="b" /> and edge radius <paramref name="r" />.
		/// </summary>
		public static float sdRoundBox(float3 p, float3 b, float r)
		{
			var q = abs(p) - b;
			var outside = length(max(q, 0f)) + min(max(q.x, max(q.y, q.z)), 0f);
			return r - outside;
		}

		/// <summary>
		///   Torus in the XY plane with major/minor radii <paramref name="t" /> = (R, r).
		/// </summary>
		public static float sdTorus(float3 p, float2 t)
		{
			var q = float2(length(p.xy) - t.x, p.z);
			return t.y - length(q);
		}

		/// <summary>
		///   Infinite cylinder along Y with radius <paramref name="r" />.
		/// </summary>
		public static float sdCylinderInfY(float3 p, float r)
		{
			return r - length(p.xz);
		}

		/// <summary>
		///   Capped cylinder along Y: half-height <paramref name="h" />, radius <paramref name="r" />.
		/// </summary>
		public static float sdCappedCylinderY(float3 p, float h, float r)
		{
			var d = abs(new float2(length(p.xz), p.y)) - new float2(r, h);
			var outside = min(max(d.x, d.y), 0f) + length(max(d, 0f));
			return -outside;
		}

		/// <summary>
		///   Capsule between endpoints <paramref name="a" /> and <paramref name="b" /> with radius <paramref name="r" />.
		/// </summary>
		public static float sdCapsule(float3 p, float3 a, float3 b, float r)
		{
			var pa = p - a;
			var ba = b - a;
			var h = clamp(dot(pa, ba) / dot(ba, ba), 0f, 1f);
			return r - length(pa - (ba * h));
		}

		/// <summary>
		///   Ellipsoid centered at origin with radii <paramref name="r" /> along axes (approximate SDF).
		/// </summary>
		public static float sdEllipsoid(float3 p, float3 r)
		{
			var k0 = length(p / r);
			var k1 = length(p / (r * r));
			var outside = k0 - 1f;
			// IQ approximation: k0*(k0-1)/k1 is outside-positive distance
			var d = k0 * (k0 - 1f) / max(k1, 1e-8f);
			return -d;
		}

		/// <summary>
		///   Regular octahedron centered at origin with size parameter <paramref name="s" /> (approximate SDF).
		/// </summary>
		public static float sdOctahedron(float3 p, float s)
		{
			var w = abs(p);
			// Approximate: outside-positive distance scaled by 1/sqrt(3)
			var outside = w.x + w.y + w.z - s;
			return -(outside * 0.57735026919f);
		}

		/// <summary>
		///   Triangular prism with equilateral cross-section. <paramref name="h" /> = (half-width, half-height along Z).
		/// </summary>
		public static float sdTriPrism(float3 p, float2 h)
		{
			var q = abs(p);
			var d1 = q.z - h.y;
			var d2 = max((q.x * 0.86602540378f) + (p.y * 0.5f), -p.y) - (h.x * 0.5f);
			var outside = max(d1, d2);
			return -outside;
		}

		/// <summary>
		///   Hexagonal prism. <paramref name="h" /> = (in-radius in XY, half-height along Z).
		/// </summary>
		public static float sdHexPrism(float3 p, float2 h)
		{
			var q = abs(p);
			var d1 = q.z - h.y;
			var d2 = max((q.x * 0.86602540378f) + (q.y * 0.5f), q.y) - h.x;
			var outside = max(d1, d2);
			return -outside;
		}

		/// <summary>
		///   Round cone (conical frustum) along Y from radius <paramref name="r1" /> at y=0 to <paramref name="r2" /> at y=h.
		/// </summary>
		public static float sdRoundConeY(float3 p, float r1, float r2, float h)
		{
			var q = new float2(length(p.xz), p.y);
			var b = (r1 - r2) / max(h, 1e-8f);
			var a = sqrt(max(1f - (b * b), 0f));
			var k = dot(q, new float2(-b, a));
			float outside;
			if (k < 0f)
				outside = length(q) - r1;
			else if (k > a * h)
				outside = length(q - new float2(0f, h)) - r2;
			else
				outside = dot(q, new float2(a, b)) - r1;
			return -outside;
		}

		/// <summary>
		///   Infinite cylinder along X (inside-positive).
		/// </summary>
		public static float sdCylinderInfX(float3 p, float r)
		{
			return r - length(p.yz);
		}

		/// <summary>
		///   Infinite cylinder along Z (inside-positive).
		/// </summary>
		public static float sdCylinderInfZ(float3 p, float r)
		{
			return r - length(p.xy);
		}

		/// <summary>
		///   Capped cylinder along X: half-height <paramref name="h" />, radius <paramref name="r" />.
		/// </summary>
		public static float sdCappedCylinderX(float3 p, float h, float r)
		{
			var d = abs(new float2(length(p.yz), p.x)) - new float2(r, h);
			var outside = min(max(d.x, d.y), 0f) + length(max(d, 0f));
			return -outside;
		}

		/// <summary>
		///   Capped cylinder along Z: half-height <paramref name="h" />, radius <paramref name="r" />.
		/// </summary>
		public static float sdCappedCylinderZ(float3 p, float h, float r)
		{
			var d = abs(new float2(length(p.xy), p.z)) - new float2(r, h);
			var outside = min(max(d.x, d.y), 0f) + length(max(d, 0f));
			return -outside;
		}
	}
}
