namespace Voxels.Core.Procedural
{
	using Spatial;
	using Unity.Burst;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using UnityEngine;
	using static Unity.Mathematics.math;
	using static VoxelConstants;

	public sealed class SimpleNoiseVoxelGenerator : ProceduralVoxelGeneratorBehaviour
	{
		[SerializeField]
		float scale = 0.1f;

		[SerializeField]
		uint seed = 1;

		public override JobHandle Schedule(
			MinMaxAABB bounds,
			float voxelSize,
			VoxelVolumeData data,
			JobHandle inputDeps
		)
		{
			inputDeps = new SNoiseJob
			{
				bounds = bounds,
				voxelSize = voxelSize,
				volumeData = data,
				scale = scale,
				seed = seed,
			}.Schedule(VOLUME_LENGTH, inputDeps);

			return inputDeps;
		}

		[BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
		struct SNoiseJob : IJobFor
		{
			public float voxelSize;
			public MinMaxAABB bounds;
			public VoxelVolumeData volumeData;

			public float scale;
			public uint seed;

			public void Execute(int index)
			{
				// Reverse of linear index mapping: index = (x << X_SHIFT) + (y << Y_SHIFT) + z
				var x = (index >> X_SHIFT) & CHUNK_SIZE_MINUS_ONE;
				var y = (index >> Y_SHIFT) & CHUNK_SIZE_MINUS_ONE;
				var z = index & CHUNK_SIZE_MINUS_ONE;

				float3 localCoord = int3(x, y, z);

				var coord = bounds.Min + (localCoord * voxelSize);

				var noiseValue = noise.cnoise(float4(coord * scale, seed));

				volumeData.sdfVolume[index] = (sbyte)clamp(noiseValue * 127, -127, 127);
			}
		}
	}
}
