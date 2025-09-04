namespace Voxels.Core.Meshing
{
	using System.Collections.Generic;
	using UnityEngine;
	using UnityEngine.Rendering;

	public static class ManagedMeshCombine
	{
		public static Mesh CombineIntoNewMesh(CombineInstance[] combines)
		{
			var result = new Mesh();
			CombineInto(result, combines);
			return result;
		}

		public static void CombineInto(Mesh target, CombineInstance[] combines)
		{
			// Pre-scan for total vertex count to pick index format
			var estimatedTotalVerts = 0;
			for (var i = 0; i < combines.Length; i++)
			{
				var src = combines[i].mesh;
				if (!src)
					continue;
				estimatedTotalVerts += src.vertexCount;
			}

			target.Clear();
			target.indexFormat = estimatedTotalVerts > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

			// Accumulate attributes
			var vertices = new List<Vector3>(estimatedTotalVerts);
			var normals = new List<Vector3>(estimatedTotalVerts);
			var tangents = new List<Vector4>(estimatedTotalVerts);
			var colors32 = new List<Color32>(estimatedTotalVerts);
			var uv0 = new List<Vector2>(estimatedTotalVerts);
			var uv1 = new List<Vector2>(estimatedTotalVerts);
			var uv2 = new List<Vector2>(estimatedTotalVerts);
			var uv3 = new List<Vector2>(estimatedTotalVerts);
			var triangles = new List<int>(estimatedTotalVerts * 3);

			var combinedBounds = new Bounds(Vector3.zero, Vector3.zero);
			var haveBounds = false;

			var vertexOffset = 0;
			for (var i = 0; i < combines.Length; i++)
			{
				var ci = combines[i];
				var src = ci.mesh;
				if (!src)
					continue;

				var srcVertices = src.vertices;
				var srcNormals = src.normals;
				var srcTangents = src.tangents;
				var srcColors32 = src.colors32;
				var srcUv0 = src.uv;
				var srcUv1 = src.uv2;
				var srcUv2 = src.uv3;
				var srcUv3 = src.uv4;

				var normalMatrix = ci.transform.inverse.transpose;

				// Vertices and attributes
				for (var v = 0; v < srcVertices.Length; v++)
				{
					var p = ci.transform.MultiplyPoint3x4(srcVertices[v]);
					vertices.Add(p);

					if (srcNormals != null && srcNormals.Length == srcVertices.Length)
					{
						var n = normalMatrix.MultiplyVector(srcNormals[v]).normalized;
						normals.Add(n);
					}

					if (srcTangents != null && srcTangents.Length == srcVertices.Length)
					{
						var t = srcTangents[v];
						var tv = new Vector3(t.x, t.y, t.z);
						tv = normalMatrix.MultiplyVector(tv).normalized;
						tangents.Add(new Vector4(tv.x, tv.y, tv.z, t.w));
					}

					if (srcColors32 != null && srcColors32.Length == srcVertices.Length)
						colors32.Add(srcColors32[v]);

					if (srcUv0 != null && srcUv0.Length == srcVertices.Length)
						uv0.Add(srcUv0[v]);
					if (srcUv1 != null && srcUv1.Length == srcVertices.Length)
						uv1.Add(srcUv1[v]);
					if (srcUv2 != null && srcUv2.Length == srcVertices.Length)
						uv2.Add(srcUv2[v]);
					if (srcUv3 != null && srcUv3.Length == srcVertices.Length)
						uv3.Add(srcUv3[v]);
				}

				// Indices for selected submesh (merge into single submesh)
				var sub = Mathf.Clamp(ci.subMeshIndex, 0, src.subMeshCount - 1);
				var srcIndices = src.GetIndices(sub, applyBaseVertex: false);
				for (var t = 0; t < srcIndices.Length; t++)
					triangles.Add(srcIndices[t] + vertexOffset);

				// Bounds
				var b = src.bounds;
				var center = ci.transform.MultiplyPoint3x4(b.center);
				var extents = b.extents;
				// Approximate transformed extents using absolute of rotation-scale columns
				var m = ci.transform;
				var ax = new Vector3(Mathf.Abs(m.m00), Mathf.Abs(m.m01), Mathf.Abs(m.m02));
				var ay = new Vector3(Mathf.Abs(m.m10), Mathf.Abs(m.m11), Mathf.Abs(m.m12));
				var az = new Vector3(Mathf.Abs(m.m20), Mathf.Abs(m.m21), Mathf.Abs(m.m22));
				var newExtents = new Vector3(
					Vector3.Dot(ax, extents),
					Vector3.Dot(ay, extents),
					Vector3.Dot(az, extents)
				);
				var tb = new Bounds(center, newExtents * 2f);
				if (!haveBounds)
				{
					combinedBounds = tb;
					haveBounds = true;
				}
				else
					combinedBounds.Encapsulate(tb);

				vertexOffset += srcVertices.Length;
			}

			// Assign buffers
			target.SetVertices(vertices);
			if (normals.Count == vertices.Count)
				target.SetNormals(normals);
			if (tangents.Count == vertices.Count)
				target.SetTangents(tangents);
			if (colors32.Count == vertices.Count)
				target.SetColors(colors32);
			if (uv0.Count == vertices.Count)
				target.SetUVs(0, uv0);
			if (uv1.Count == vertices.Count)
				target.SetUVs(1, uv1);
			if (uv2.Count == vertices.Count)
				target.SetUVs(2, uv2);
			if (uv3.Count == vertices.Count)
				target.SetUVs(3, uv3);

			target.subMeshCount = 1;
			target.SetIndices(triangles, MeshTopology.Triangles, 0, calculateBounds: false);
			target.bounds = combinedBounds;
		}
	}
}
