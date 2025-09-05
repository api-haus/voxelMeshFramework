namespace Voxels.Core.Meshing
{
	using ThirdParty.SurfaceNets;
	using Unity.Burst;
	using Unity.Collections;
	using UnityEngine;
	using UnityEngine.Rendering;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Unity.Mathematics.math;

	public static class SharedStaticMeshingResources
	{
		static readonly SharedStatic<EdgeTableShared> s_sharedStatic =
			SharedStatic<EdgeTableShared>.GetOrCreate<EdgeTableShared>();

		public static ref NativeArray<ushort> EdgeTable => ref s_sharedStatic.Data.edgeTable;

		public static ref NativeArray<VertexAttributeDescriptor> VertexAttributes =>
			ref s_sharedStatic.Data.vertexAttributes;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void Initialize()
		{
			using var _ = SharedStaticMeshingResources_Initialize.Auto();

			if (!EdgeTable.IsCreated)
			{
				EdgeTable = new(256, Allocator.Domain);
				FillEdgeTable();
			}

			if (!VertexAttributes.IsCreated)
				VertexAttributes = new(Vertex.VertexFormat, Allocator.Domain);

			// Application.quitting += Release;
		}

		static void FillEdgeTable()
		{
			using var _ = SharedStaticMeshingResources_FillEdgeTable.Auto();
			var cubeEdges = new int[24];
			var k = 0;
			for (var i = 0; i < 8; ++i)
			for (var j = 1; j <= 4; j <<= 1)
			{
				var p = i ^ j;
				if (i <= p)
				{
					cubeEdges[k++] = i;
					cubeEdges[k++] = p;
				}
			}

			for (var i = 0; i < 256; ++i)
			{
				var em = 0;
				for (var j = 0; j < 24; j += 2)
				{
					var a = (i & (1 << cubeEdges[j])) != 0;
					var b = (i & (1 << cubeEdges[j + 1])) != 0;
					// em |= a != b ? 1 << (j >> 1) : 0;
					em |= select(
						//
						0,
						1 << (j >> 1),
						a != b
					);
				}

				EdgeTable[i] = (ushort)em;
			}
		}

		static void Release()
		{
			// TODO: Persistent allocator, and clear before domain reload
			// EdgeTable.Dispose();
			// VertexAttributes.Dispose();
		}

		public struct EdgeTableShared
		{
			public NativeArray<ushort> edgeTable;
			public NativeArray<VertexAttributeDescriptor> vertexAttributes;
		}
	}
}
