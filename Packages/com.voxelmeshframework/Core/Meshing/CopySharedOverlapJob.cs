namespace Voxels.Core.Meshing
{
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Jobs;
	using static Diagnostics.VoxelProfiler.Marks;
	using static VoxelConstants;

	/// <summary>
	///   Copies the 2-voxel shared overlap region from a source chunk to an adjacent destination chunk
	///   along the specified axis. Assumes axis-aligned chunks and EFFECTIVE_CHUNK_SIZE spacing.
	/// </summary>
	[BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
	public struct CopySharedOverlapJob : IJob
	{
		[ReadOnly]
		public NativeArray<sbyte> sourceSdf;

		[ReadOnly]
		public NativeArray<byte> sourceMaterials;

		public NativeArray<sbyte> destSdf;

		public NativeArray<byte> destMaterials;

		/// <summary>
		///   Axis of adjacency: 0 = X, 1 = Y, 2 = Z.
		///   Always copies from the lower-coord chunk to the higher-coord chunk along that axis.
		/// </summary>
		public byte axis;

		public void Execute()
		{
			using var __ = VoxelStampSystem_CopySharedOverlap.Auto();
			const int overlap = CHUNK_OVERLAP; // 2
			const int sourceHighStart = CHUNK_SIZE - overlap; // 30 when CHUNK_SIZE=32, overlap=2
			const int destLowStart = 0; // 0..1 are the shared low-side indices

			switch (axis)
			{
				case 0: // X
					for (var y = 0; y < CHUNK_SIZE; y++)
					for (var z = 0; z < CHUNK_SIZE; z++)
					for (var o = 0; o < overlap; o++)
					{
						var sx = sourceHighStart + o;
						var dx = destLowStart + o;
						var sPtr = (sx << X_SHIFT) + (y << Y_SHIFT) + z;
						var dPtr = (dx << X_SHIFT) + (y << Y_SHIFT) + z;

						destSdf[dPtr] = sourceSdf[sPtr];
						destMaterials[dPtr] = sourceMaterials[sPtr];
					}

					break;

				case 1: // Y
					for (var x = 0; x < CHUNK_SIZE; x++)
					for (var z = 0; z < CHUNK_SIZE; z++)
					for (var o = 0; o < overlap; o++)
					{
						var sy = sourceHighStart + o;
						var dy = destLowStart + o;
						var sPtr = (x << X_SHIFT) + (sy << Y_SHIFT) + z;
						var dPtr = (x << X_SHIFT) + (dy << Y_SHIFT) + z;

						destSdf[dPtr] = sourceSdf[sPtr];
						destMaterials[dPtr] = sourceMaterials[sPtr];
					}

					break;

				default: // 2: Z
					for (var x = 0; x < CHUNK_SIZE; x++)
					for (var y = 0; y < CHUNK_SIZE; y++)
					for (var o = 0; o < overlap; o++)
					{
						var sz = sourceHighStart + o;
						var dz = destLowStart + o;
						var sPtr = (x << X_SHIFT) + (y << Y_SHIFT) + sz;
						var dPtr = (x << X_SHIFT) + (y << Y_SHIFT) + dz;

						destSdf[dPtr] = sourceSdf[sPtr];
						destMaterials[dPtr] = sourceMaterials[sPtr];
					}

					break;
			}
		}
	}
}
