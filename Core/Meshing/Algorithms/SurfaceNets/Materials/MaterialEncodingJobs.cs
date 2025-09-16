namespace Voxels.Core.Meshing.Algorithms.SurfaceNets.Materials
{
	using System.Runtime.CompilerServices;
	using ThirdParty.SurfaceNets;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Jobs;
	using Unity.Mathematics;
	using UnityEngine;
	using static Unity.Mathematics.math;

	static class MaterialEncodingUtil
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int ToLinearIndex(int x, int y, int z, int chunkSize)
		{
			return (x * chunkSize * chunkSize) + (y * chunkSize) + z;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int3 GetCellFromPosition(float3 position, float voxelSize, int chunkSize)
		{
			var fx = (int)floor(position.x / voxelSize);
			var fy = (int)floor(position.y / voxelSize);
			var fz = (int)floor(position.z / voxelSize);
			var maxBase = chunkSize - 2; // we access +1 neighbor
			return new int3(clamp(fx, 0, maxBase), clamp(fy, 0, maxBase), clamp(fz, 0, maxBase));
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void SampleCellCornerMaterials(
			in NativeArray<byte> materials,
			int3 cellBase,
			int chunkSize,
			ref byte m0,
			ref byte m1,
			ref byte m2,
			ref byte m3,
			ref byte m4,
			ref byte m5,
			ref byte m6,
			ref byte m7
		)
		{
			var cx = cellBase.x;
			var cy = cellBase.y;
			var cz = cellBase.z;

			var i000 = ToLinearIndex(cx + 0, cy + 0, cz + 0, chunkSize);
			var i100 = ToLinearIndex(cx + 1, cy + 0, cz + 0, chunkSize);
			var i010 = ToLinearIndex(cx + 0, cy + 1, cz + 0, chunkSize);
			var i110 = ToLinearIndex(cx + 1, cy + 1, cz + 0, chunkSize);
			var i001 = ToLinearIndex(cx + 0, cy + 0, cz + 1, chunkSize);
			var i101 = ToLinearIndex(cx + 1, cy + 0, cz + 1, chunkSize);
			var i011 = ToLinearIndex(cx + 0, cy + 1, cz + 1, chunkSize);
			var i111 = ToLinearIndex(cx + 1, cy + 1, cz + 1, chunkSize);

			m0 = materials[i000];
			m1 = materials[i100];
			m2 = materials[i010];
			m3 = materials[i110];
			m4 = materials[i001];
			m5 = materials[i101];
			m6 = materials[i011];
			m7 = materials[i111];
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Color32 EncodeCornerSumSplat4(
			byte m0,
			byte m1,
			byte m2,
			byte m3,
			byte m4,
			byte m5,
			byte m6,
			byte m7
		)
		{
			float w0 = 0f,
				w1 = 0f,
				w2 = 0f,
				w3 = 0f;
			var count = 0f;

			acc(m0);
			acc(m1);
			acc(m2);
			acc(m3);
			acc(m4);
			acc(m5);
			acc(m6);
			acc(m7);

			if (count > 0f)
			{
				var inv = 255f / count;
				return new Color32((byte)(w0 * inv), (byte)(w1 * inv), (byte)(w2 * inv), (byte)(w3 * inv));
			}

			return default;

			void acc(byte mat)
			{
				if (mat == 0)
					return;
				var ch = (mat - 1) & 3;
				switch (ch)
				{
					case 0:
						w0 += 1f;
						break;
					case 1:
						w1 += 1f;
						break;
					case 2:
						w2 += 1f;
						break;
					default:
						w3 += 1f;
						break;
				}

				count += 1f;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe byte SelectMajorityMaterial(
			byte m0,
			byte m1,
			byte m2,
			byte m3,
			byte m4,
			byte m5,
			byte m6,
			byte m7
		)
		{
			// Determine the mode among up to eight values (ignoring 0)
			byte best = 0;
			var bestCount = 0;
			var vals = stackalloc byte[8] { m0, m1, m2, m3, m4, m5, m6, m7 };
			for (var i = 0; i < 8; i++)
			{
				var v = vals[i];
				if (v == 0)
					continue;
				var cnt = 1;
				for (var j = i + 1; j < 8; j++)
					if (vals[j] == v)
						cnt++;
				if (cnt > bestCount)
				{
					bestCount = cnt;
					best = v;
				}
			}

			return best;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void AccumulateCornerSumWeights(
			byte m0,
			byte m1,
			byte m2,
			byte m3,
			byte m4,
			byte m5,
			byte m6,
			byte m7,
			int channelsCount,
			float* weightsOut
		)
		{
			for (var i = 0; i < channelsCount; i++)
				weightsOut[i] = 0f;
			var contributing = 0f;

			acc(m0);
			acc(m1);
			acc(m2);
			acc(m3);
			acc(m4);
			acc(m5);
			acc(m6);
			acc(m7);

			if (contributing > 0f)
			{
				var inv = 1f / contributing;
				for (var i = 0; i < channelsCount; i++)
					weightsOut[i] *= inv;
			}

			return;

			void acc(byte mat)
			{
				if (mat == 0)
					return;
				var ch = (mat - 1) % channelsCount;
				weightsOut[ch] += 1f;
				contributing += 1f;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static unsafe void WriteWeightsToVertex(float* weights, int channelsCount, ref Vertex v)
		{
			// Pack groups of 4 weights into a single float (MicroSplat-like packing)
			float4 color = default;
			float4 sc0 = default;
			float4 sc1 = default;
			float4 sc2 = default;
			float4 sc3 = default;
			float4 sc4 = default;
			float4 sc5 = default;

			var groupCount = (channelsCount + 3) >> 2; // ceil(channels/4)
			for (var g = 0; g < groupCount; g++)
			{
				var baseIdx = g << 2;
				var grp = new float4(
					baseIdx + 0 < channelsCount ? weights[baseIdx + 0] : 0f,
					baseIdx + 1 < channelsCount ? weights[baseIdx + 1] : 0f,
					baseIdx + 2 < channelsCount ? weights[baseIdx + 2] : 0f,
					baseIdx + 3 < channelsCount ? weights[baseIdx + 3] : 0f
				);
				var packed = EncodeToFloat(grp);

				switch (g)
				{
					case 0:
						color.x = packed;
						break;
					case 1:
						color.y = packed;
						break;
					case 2:
						color.z = packed;
						break;
					case 3:
						color.w = packed;
						break;
					case 4:
						sc0.x = packed;
						break;
					case 5:
						sc0.y = packed;
						break;
					case 6:
						sc0.z = packed;
						break;
					case 7:
						sc0.w = packed;
						break;
					case 8:
						sc1.x = packed;
						break;
					case 9:
						sc1.y = packed;
						break;
					case 10:
						sc1.z = packed;
						break;
					case 11:
						sc1.w = packed;
						break;
					case 12:
						sc2.x = packed;
						break;
					case 13:
						sc2.y = packed;
						break;
					case 14:
						sc2.z = packed;
						break;
					case 15:
						sc2.w = packed;
						break;
					case 16:
						sc3.x = packed;
						break;
					case 17:
						sc3.y = packed;
						break;
					case 18:
						sc3.z = packed;
						break;
					case 19:
						sc3.w = packed;
						break;
					case 20:
						sc4.x = packed;
						break;
					case 21:
						sc4.y = packed;
						break;
					case 22:
						sc4.z = packed;
						break;
					case 23:
						sc4.w = packed;
						break;
					default:
						sc5.x = packed;
						break; // extremely rare overflow
				}
			}

			v.color = color;
			v.splatControl0 = sc0;
			v.splatControl1 = sc1;
			v.splatControl2 = sc2;
			v.splatControl3 = sc3;
			v.splatControl4 = sc4;
			v.splatControl5 = sc5;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float EncodeToFloat(float4 enc)
		{
			var e = (uint4)round(saturate(enc) * 255f);
			var v = (e.x << 24) | (e.y << 16) | (e.z << 8) | e.w;
			return v / (256f * 256f * 256f * 256f);
		}
	}

	[BurstCompile]
	public struct EncodeMaterialsValueRJob : IJob
	{
		[ReadOnly]
		public NativeArray<byte> materials;

		public NativeList<Vertex> vertices;

		public float voxelSize;
		public int chunkSize;

		public void Execute()
		{
			var arr = vertices.AsArray();
			for (var i = 0; i < arr.Length; i++)
			{
				var v = arr[i];
				var cell = MaterialEncodingUtil.GetCellFromPosition(v.position, voxelSize, chunkSize);
				byte m0 = 0,
					m1 = 0,
					m2 = 0,
					m3 = 0,
					m4 = 0,
					m5 = 0,
					m6 = 0,
					m7 = 0;
				MaterialEncodingUtil.SampleCellCornerMaterials(
					materials,
					cell,
					chunkSize,
					ref m0,
					ref m1,
					ref m2,
					ref m3,
					ref m4,
					ref m5,
					ref m6,
					ref m7
				);
				var id = MaterialEncodingUtil.SelectMajorityMaterial(m0, m1, m2, m3, m4, m5, m6, m7);
				v.color = new float4(id / 255f, 0f, 0f, 1f);
				arr[i] = v;
			}
		}
	}

	[BurstCompile]
	public struct EncodeMaterialsPaletteJob : IJob
	{
		[ReadOnly]
		public NativeArray<byte> materials;

		[ReadOnly]
		public NativeArray<Color32> palette;

		public NativeList<Vertex> vertices;

		public float voxelSize;
		public int chunkSize;

		public void Execute()
		{
			var arr = vertices.AsArray();
			for (var i = 0; i < arr.Length; i++)
			{
				var v = arr[i];
				var cell = MaterialEncodingUtil.GetCellFromPosition(v.position, voxelSize, chunkSize);
				byte m0 = 0,
					m1 = 0,
					m2 = 0,
					m3 = 0,
					m4 = 0,
					m5 = 0,
					m6 = 0,
					m7 = 0;
				MaterialEncodingUtil.SampleCellCornerMaterials(
					materials,
					cell,
					chunkSize,
					ref m0,
					ref m1,
					ref m2,
					ref m3,
					ref m4,
					ref m5,
					ref m6,
					ref m7
				);
				var id = MaterialEncodingUtil.SelectMajorityMaterial(m0, m1, m2, m3, m4, m5, m6, m7);
				if (palette.IsCreated && id < palette.Length)
				{
					var c32 = palette[id];
					v.color = new float4(c32.r, c32.g, c32.b, c32.a) / 255f;
				}
				else
				{
					var vnorm = id / 255f;
					v.color = new float4(vnorm, vnorm, vnorm, 1f);
				}

				arr[i] = v;
			}
		}
	}

	[BurstCompile]
	public struct EncodeMaterialsSplat4Job : IJob
	{
		[ReadOnly]
		public NativeArray<byte> materials;

		public NativeList<Vertex> vertices;

		public float voxelSize;
		public int chunkSize;

		public unsafe void Execute()
		{
			var arr = vertices.AsArray();
			for (var i = 0; i < arr.Length; i++)
			{
				var v = arr[i];
				var cell = MaterialEncodingUtil.GetCellFromPosition(v.position, voxelSize, chunkSize);
				byte m0 = 0,
					m1 = 0,
					m2 = 0,
					m3 = 0,
					m4 = 0,
					m5 = 0,
					m6 = 0,
					m7 = 0;
				MaterialEncodingUtil.SampleCellCornerMaterials(
					materials,
					cell,
					chunkSize,
					ref m0,
					ref m1,
					ref m2,
					ref m3,
					ref m4,
					ref m5,
					ref m6,
					ref m7
				);
				var weights = stackalloc float[4];
				MaterialEncodingUtil.AccumulateCornerSumWeights(m0, m1, m2, m3, m4, m5, m6, m7, 4, weights);
				MaterialEncodingUtil.WriteWeightsToVertex(weights, 4, ref v);
				arr[i] = v;
			}
		}
	}

	// Placeholder implementations for future multi-stream support.
	// For now, they use the same 4-channel encoding as Splat4 and can be expanded later
	// when additional vertex streams are introduced.
	[BurstCompile]
	public struct EncodeMaterialsSplat8Job : IJob
	{
		[ReadOnly]
		public NativeArray<byte> materials;

		public NativeList<Vertex> vertices;

		public float voxelSize;
		public int chunkSize;

		public unsafe void Execute()
		{
			var arr = vertices.AsArray();
			for (var i = 0; i < arr.Length; i++)
			{
				var v = arr[i];
				var cell = MaterialEncodingUtil.GetCellFromPosition(v.position, voxelSize, chunkSize);
				byte m0 = 0,
					m1 = 0,
					m2 = 0,
					m3 = 0,
					m4 = 0,
					m5 = 0,
					m6 = 0,
					m7 = 0;
				MaterialEncodingUtil.SampleCellCornerMaterials(
					materials,
					cell,
					chunkSize,
					ref m0,
					ref m1,
					ref m2,
					ref m3,
					ref m4,
					ref m5,
					ref m6,
					ref m7
				);
				var weights = stackalloc float[8];
				MaterialEncodingUtil.AccumulateCornerSumWeights(m0, m1, m2, m3, m4, m5, m6, m7, 8, weights);
				MaterialEncodingUtil.WriteWeightsToVertex(weights, 8, ref v);
				arr[i] = v;
			}
		}
	}

	[BurstCompile]
	public struct EncodeMaterialsSplat12Job : IJob
	{
		[ReadOnly]
		public NativeArray<byte> materials;

		public NativeList<Vertex> vertices;

		public float voxelSize;
		public int chunkSize;

		public unsafe void Execute()
		{
			var arr = vertices.AsArray();
			for (var i = 0; i < arr.Length; i++)
			{
				var v = arr[i];
				var cell = MaterialEncodingUtil.GetCellFromPosition(v.position, voxelSize, chunkSize);
				byte m0 = 0,
					m1 = 0,
					m2 = 0,
					m3 = 0,
					m4 = 0,
					m5 = 0,
					m6 = 0,
					m7 = 0;
				MaterialEncodingUtil.SampleCellCornerMaterials(
					materials,
					cell,
					chunkSize,
					ref m0,
					ref m1,
					ref m2,
					ref m3,
					ref m4,
					ref m5,
					ref m6,
					ref m7
				);
				var weights = stackalloc float[12];
				MaterialEncodingUtil.AccumulateCornerSumWeights(
					m0,
					m1,
					m2,
					m3,
					m4,
					m5,
					m6,
					m7,
					12,
					weights
				);
				MaterialEncodingUtil.WriteWeightsToVertex(weights, 12, ref v);
				arr[i] = v;
			}
		}
	}

	[BurstCompile]
	public struct EncodeMaterialsSplat16Job : IJob
	{
		[ReadOnly]
		public NativeArray<byte> materials;

		public NativeList<Vertex> vertices;

		public float voxelSize;
		public int chunkSize;

		public unsafe void Execute()
		{
			var arr = vertices.AsArray();
			for (var i = 0; i < arr.Length; i++)
			{
				var v = arr[i];
				var cell = MaterialEncodingUtil.GetCellFromPosition(v.position, voxelSize, chunkSize);
				byte m0 = 0,
					m1 = 0,
					m2 = 0,
					m3 = 0,
					m4 = 0,
					m5 = 0,
					m6 = 0,
					m7 = 0;
				MaterialEncodingUtil.SampleCellCornerMaterials(
					materials,
					cell,
					chunkSize,
					ref m0,
					ref m1,
					ref m2,
					ref m3,
					ref m4,
					ref m5,
					ref m6,
					ref m7
				);
				var weights = stackalloc float[16];
				MaterialEncodingUtil.AccumulateCornerSumWeights(
					m0,
					m1,
					m2,
					m3,
					m4,
					m5,
					m6,
					m7,
					16,
					weights
				);
				MaterialEncodingUtil.WriteWeightsToVertex(weights, 16, ref v);
				arr[i] = v;
			}
		}
	}
}
