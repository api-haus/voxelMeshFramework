namespace Voxels.Core.Procedural.Generators
{
	using Spatial;
	using Unity.Burst;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using static Unity.Mathematics.math;
	using static VoxelConstants;

	public struct HemisphereIslandGenerator : IProceduralVoxelGenerator
	{
		public float radius;
		public float planeY;
		public int sdfSamplesPerVoxel;

		public JobHandle Schedule(
			MinMaxAABB localBounds,
			float4x4 transform,
			float voxelSize,
			VoxelVolumeData data,
			JobHandle inputDeps
		)
		{
			var sdfScale = max(1, sdfSamplesPerVoxel) / voxelSize;

			inputDeps = new SdfJob
			{
				ltw = transform,
				bounds = localBounds,
				voxelSize = voxelSize,
				volumeData = data,
				radius = radius,
				planeY = planeY,
				sdfScale = sdfScale,
			}.Schedule(VOLUME_LENGTH, inputDeps);

			inputDeps = new MaterialJob
			{
				ltw = transform,
				bounds = localBounds,
				voxelSize = voxelSize,
				volumeData = data,
				sdfScale = sdfScale,
				material = 1,
			}.Schedule(VOLUME_LENGTH, inputDeps);

			return inputDeps;
		}

		[BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
		struct SdfJob : IJobFor
		{
			public float voxelSize;
			public MinMaxAABB bounds;
			public VoxelVolumeData volumeData;
			public float4x4 ltw;

			public float radius;
			public float planeY;
			public float sdfScale;

			static float SdSolidAngle(float3 p, float2 c, float ra)
			{
				// c = float2(sin(theta), cos(theta))
				var q = float2(length(p.xz), p.y);
				var l = length(q) - ra;
				var h = clamp(dot(q, c), 0f, ra);
				var qc = q - (c * h);
				var m = length(qc);
				var s = sign((c.y * q.x) - (c.x * q.y));
				return max(l, m * s);
			}

			static float SdOctahedron(float3 p, float s)
			{
				p = abs(p);
				var m = p.x + p.y + p.z - s;
				float3 q;
				if (3f * p.x < m)
					q = p.xyz;
				else if (3f * p.y < m)
					q = p.yzx;
				else if (3f * p.z < m)
					q = p.zxy;
				else
					return m * 0.57735027f; // 1/sqrt(3)

				var k = clamp(0.5f * (q.z - q.y + s), 0f, s);
				return length(new float3(q.x, q.y - s + k, q.z - k));
			}

			public void Execute(int index)
			{
				var x = (index >> X_SHIFT) & CHUNK_SIZE_MINUS_ONE;
				var y = (index >> Y_SHIFT) & CHUNK_SIZE_MINUS_ONE;
				var z = index & CHUNK_SIZE_MINUS_ONE;

				float3 localCoord = int3(x, y, z);
				var coord = bounds.Min + (localCoord * voxelSize);
				coord = transform(ltw, coord);

				var center = new float3(EFFECTIVE_CHUNK_SIZE, planeY, EFFECTIVE_CHUNK_SIZE);
				var toCenter = coord - center;
				var d = length(toCenter);
				var sSphere = radius - d;
				var sPlane = planeY - coord.y;
				var sHemisphere = min(sSphere, sPlane);

				// Solid angle cap protruding from the top of the sphere
				var apex = center + float3(0f, planeY * .56f, 0f);
				var p = coord - apex;
				var theta = radians(23f);
				var c = float2(sin(theta), cos(theta));
				var ra = radius * 0.9f;
				var sAngleInsidePositive = -SdSolidAngle(p, c, ra);

				// Octahedral cap on the top (adds faceted peak)
				var sOctInsidePositive = -SdOctahedron(p, radius * 0.7f);

				// Union: hemisphere with protruding caps
				var sUnion = max(sHemisphere, sAngleInsidePositive);
				sUnion = max(sUnion, sOctInsidePositive);

				var sdfByte = clamp(sUnion * sdfScale, -127f, 127f);
				volumeData.sdfVolume[index] = (sbyte)sdfByte;
			}
		}

		[BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
		struct MaterialJob : IJobFor
		{
			public float voxelSize;
			public MinMaxAABB bounds;
			public VoxelVolumeData volumeData;
			public float4x4 ltw;
			public float sdfScale;
			public byte material;

			public void Execute(int index)
			{
				var sWorld = volumeData.sdfVolume[index] / sdfScale;
				volumeData.materials[index] = sWorld >= 0f ? material : MATERIAL_AIR;
			}
		}
	}
}
