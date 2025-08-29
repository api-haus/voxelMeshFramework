namespace Voxels.ThirdParty.SurfaceNets
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
		public Color32 color;

		public static readonly VertexAttributeDescriptor[] VertexFormat =
		{
			new(VertexAttribute.Position),
			new(VertexAttribute.Normal),
			new(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
		};
	}
}
