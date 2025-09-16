namespace Voxels.Core.ThirdParty.SurfaceNets
{
	using System.Runtime.InteropServices;
	using Unity.Mathematics;
	using UnityEngine;
	using UnityEngine.Rendering;

	[StructLayout(LayoutKind.Sequential)]
	public struct Vertex
	{
		public float3 position;
		public float3 normal;
		public Vector4 tangent;
		public float4 color;
		public float2 uv;
		public float4 splatControl0;
		public float4 splatControl1;
		public float4 splatControl2;
		public float4 splatControl3;
		public float4 splatControl4;
		public float4 splatControl5;

		public static readonly VertexAttributeDescriptor[] VertexFormat =
		{
			new(VertexAttribute.Position),
			new(VertexAttribute.Normal),
			new(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4),
			new(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
			new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2), // UV
			new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4), // SplatControl0
			new(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4), // SplatControl1
			new(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4), // SplatControl2
			new(VertexAttribute.TexCoord4, VertexAttributeFormat.Float32, 4), // SplatControl3
			new(VertexAttribute.TexCoord5, VertexAttributeFormat.Float32, 4), // SplatControl4
			new(VertexAttribute.TexCoord6, VertexAttributeFormat.Float32, 4), // SplatControl5
		};
	}
}
