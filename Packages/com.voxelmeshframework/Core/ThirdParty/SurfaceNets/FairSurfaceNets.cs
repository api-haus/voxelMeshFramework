namespace Voxels.Core.ThirdParty.SurfaceNets
{
	using System;
	using Intrinsics;
	using Unity.Burst;
	using Unity.Burst.CompilerServices;
	using Unity.Collections;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using UnityEngine;
	using Utils;
	using static Diagnostics.VoxelProfiler.Marks;
	using static Intrinsics.NeonExt;
	using static Unity.Burst.Intrinsics.Arm.Neon;
	using static Unity.Burst.Intrinsics.X86.Sse;
	using static Unity.Burst.Intrinsics.X86.Sse2;
	using static Unity.Burst.Intrinsics.X86.Sse4_1;
	using static Unity.Burst.Intrinsics.X86.Ssse3;
	using static Unity.Mathematics.math;
	using static Voxels.Core.VoxelConstants;
	using float3 = Unity.Mathematics.float3;
	using v128 = Unity.Burst.Intrinsics.v128;

	/// <summary>
	///   Surface Nets with Surface Fairing Implementation
	///   This enhanced version of Surface Nets includes:
	///   - Material support for multi-material meshes
	///   - Surface fairing for smoother mesh output
	///   - Feature preservation at material boundaries
	///   Based on "SurfaceNets for Multi-Label Segmentations with Preservation of Sharp Boundaries"
	///   (Journal of Computer Graphics Techniques, Vol. 11, No. 1, 2022)
	/// </summary>
	[BurstCompile(
		Debug = false,
		FloatMode = FloatMode.Fast,
		OptimizeFor = OptimizeFor.Performance,
		FloatPrecision = FloatPrecision.Low,
		DisableSafetyChecks = true,
		CompileSynchronously = true
	)]
	public struct FairSurfaceNets : IJob
	{
		/// <summary>
		///   Precomputed edge table that maps corner configurations to edge crossings.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeArray<ushort> edgeTable;

		/// <summary>
		///   3D volume data representing signed distance field values.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeArray<sbyte> volume;

		/// <summary>
		///   Material IDs for each voxel (0-255).
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeArray<byte> materials;

		/// <summary>
		///   Temporary buffer used for storing vertex indices during triangulation.
		/// </summary>
		[NoAlias]
		public NativeArray<int> buffer;

		/// <summary>
		///   Output triangle indices defining the mesh connectivity.
		/// </summary>
		[NoAlias]
		public NativeList<int> indices;

		/// <summary>
		///   Output vertex data including positions, normals, and material info.
		/// </summary>
		[NoAlias]
		public NativeList<Vertex> vertices;

		/// <summary>
		///   Cell coordinates for each vertex (used for surface fairing).
		/// </summary>
		[NoAlias]
		public NativeList<int3> vertexCellCoords;

		/// <summary>
		///   Bounding box that encompasses all generated vertices.
		/// </summary>
		[NoAlias]
		public UnsafePointer<MinMaxAABB> bounds;

		/// <summary>
		///   Flag indicating whether to recalculate normals from triangle geometry.
		/// </summary>
		public bool recalculateNormals;

		/// <summary>
		///   Size of each voxel in world units.
		/// </summary>
		public float voxelSize;

		/// <summary>
		///   Enable surface fairing smoothing.
		/// </summary>
		public bool enableSurfaceFairing;

		/// <summary>
		///   Number of fairing iterations (typically 3-10).
		/// </summary>
		public int fairingIterations;

		/// <summary>
		///   Step size for fairing (typically 0.5-0.7).
		/// </summary>
		public float fairingStepSize;

		/// <summary>
		///   Cell margin to prevent vertices from reaching cell boundaries (typically 0.1).
		///   This value is scaled by voxelSize to maintain consistent behavior across different scales.
		/// </summary>
		public float cellMargin;

		/// <summary>
		///   Main execution entry point for the surface meshing algorithm.
		/// </summary>
		public void Execute()
		{
			using var _ = VoxelMeshingSystem_Perform_Fair.Auto();

			// Validate that chunk size matches the SIMD optimization requirements
			if (CHUNK_SIZE != 32)
				throw new Exception("ChunkSize must be equal to 32 to use this job");

			// Initialize output containers and reset bounding box
			bounds.Item = new MinMaxAABB(float.PositiveInfinity, float.NegativeInfinity);
			indices.Clear();
			vertices.Clear();
			vertexCellCoords.Clear();

			// Execute the main surface extraction algorithm
			ProcessVoxels();

			// Apply surface fairing if enabled
			if (enableSurfaceFairing && vertices.Length > 0)
				ApplySurfaceFairing();

			// Optionally recalculate normals from triangle geometry for higher quality
			if (recalculateNormals)
				RecalculateNormals();
		}

		/// <summary>
		///   Core voxel processing algorithm that extracts isosurfaces from volume data.
		/// </summary>
		[SkipLocalsInit]
		unsafe void ProcessVoxels()
		{
			var samples01 = stackalloc sbyte[64];
			var samples23 = stackalloc sbyte[64];

			var volumePtr = (sbyte*)volume.GetUnsafeReadOnlyPtr();
			var materialsPtr = (byte*)materials.GetUnsafeReadOnlyPtr();

			int mask0 = 0,
				mask1 = 0,
				mask2 = 0,
				mask3 = 0;

			for (var x = 0; x < CHUNK_SIZE_MINUS_ONE; x++)
			{
				(mask2, mask3) = ExtractSignBitsAndSamples(volumePtr, samples23, x);

				for (var y = 0; y < CHUNK_SIZE_MINUS_ONE; y++)
				{
					var temp = samples01;
					samples01 = samples23;
					samples23 = temp;

					mask0 = mask2;
					mask1 = mask3;

					(mask2, mask3) = ExtractSignBitsAndSamples(volumePtr, samples23, x, y);

					var masks = new v128(mask0, mask1, mask2, mask3);

					int zerosOnes;

					if (IsSse41Supported)
						zerosOnes = test_mix_ones_zeroes(masks, new v128(uint.MaxValue));
					else if (IsNeonSupported)
						zerosOnes = test_mix_ones_zeroesNEON(masks, new v128(uint.MaxValue));
					else
						zerosOnes = X86F.Sse4_1.test_mix_ones_zeroes(masks, new v128(uint.MaxValue));

					if (zerosOnes == 0)
						continue;

					int cornerMask;

					if (IsSseSupported)
						cornerMask = movemask_ps(masks) << 4;
					else
						cornerMask = X86F.Sse.movemask_ps(masks) << 4;

					var samples = stackalloc float[8];

					for (var z = 0; z < CHUNK_SIZE_MINUS_ONE; z++)
					{
						cornerMask = cornerMask >> 4;

						if (IsSse2Supported)
							masks = slli_epi32(masks, 1);
						else if (IsNeonSupported)
							masks = vshlq_n_s32(masks, 1);
						else
							masks = X86F.Sse2.slli_epi32(masks, 1);

						if (IsSseSupported)
							cornerMask |= movemask_ps(masks) << 4;
						else
							cornerMask |= X86F.Sse.movemask_ps(masks) << 4;

						if (cornerMask == 0 || cornerMask == 255)
							continue;

						int edgeMask = edgeTable[cornerMask];

						var zz = z + z;
						samples[0] = samples01[zz + 0];
						samples[1] = samples01[zz + 1];
						samples[2] = samples23[zz + 0];
						samples[3] = samples23[zz + 1];
						samples[4] = samples01[zz + 2];
						samples[5] = samples01[zz + 3];
						samples[6] = samples23[zz + 2];
						samples[7] = samples23[zz + 3];

						var pos = stackalloc int[3] { x, y, z };

						var flipTriangle = (cornerMask & 1) != 0;

						MeshSamples(pos, samples, edgeMask, flipTriangle, materialsPtr);
					}
				}
			}
		}

		/// <summary>
		///   Extracts sign bits from voxel data and loads interleaved samples for SIMD processing.
		/// </summary>
		[SkipLocalsInit]
		unsafe (int, int) ExtractSignBitsAndSamples(
			sbyte* volumePtr,
			sbyte* samples23,
			int x,
			int y = -1
		)
		{
			var shuffleReverseByteOrder = new v128(15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0);

			var ptr = volumePtr + (x << X_SHIFT) + ((y + 1) << Y_SHIFT);

			v128 lo2,
				hi2,
				lo3,
				hi3;

			if (IsSse2Supported)
			{
				lo2 = load_si128(ptr + 0);
				hi2 = load_si128(ptr + 16);
				lo3 = load_si128(ptr + 1024);
				hi3 = load_si128(ptr + 1040);
			}
			else if (IsNeonSupported)
			{
				lo2 = vld1q_u8((byte*)(ptr + 0));
				hi2 = vld1q_u8((byte*)(ptr + 16));
				lo3 = vld1q_u8((byte*)(ptr + 1024));
				hi3 = vld1q_u8((byte*)(ptr + 1040));
			}
			else
			{
				lo2 = X86F.Sse2.load_si128(ptr + 0);
				hi2 = X86F.Sse2.load_si128(ptr + 16);
				lo3 = X86F.Sse2.load_si128(ptr + 1024);
				hi3 = X86F.Sse2.load_si128(ptr + 1040);
			}

			if (IsSse2Supported)
			{
				store_si128(samples23 + 00, unpacklo_epi8(lo2, lo3));
				store_si128(samples23 + 16, unpackhi_epi8(lo2, lo3));
				store_si128(samples23 + 32, unpacklo_epi8(hi2, hi3));
				store_si128(samples23 + 48, unpackhi_epi8(hi2, hi3));
			}
			else
			{
				X86F.Sse2.store_si128(samples23 + 00, X86F.Sse2.unpacklo_epi8(lo2, lo3));
				X86F.Sse2.store_si128(samples23 + 16, X86F.Sse2.unpackhi_epi8(lo2, lo3));
				X86F.Sse2.store_si128(samples23 + 32, X86F.Sse2.unpacklo_epi8(hi2, hi3));
				X86F.Sse2.store_si128(samples23 + 48, X86F.Sse2.unpackhi_epi8(hi2, hi3));
			}

			if (IsSsse3Supported)
			{
				lo2 = shuffle_epi8(lo2, shuffleReverseByteOrder);
				lo3 = shuffle_epi8(lo3, shuffleReverseByteOrder);
				hi2 = shuffle_epi8(hi2, shuffleReverseByteOrder);
				hi3 = shuffle_epi8(hi3, shuffleReverseByteOrder);
			}
			else
			{
				lo2 = X86F.Ssse3.shuffle_epi8(lo2, shuffleReverseByteOrder);
				lo3 = X86F.Ssse3.shuffle_epi8(lo3, shuffleReverseByteOrder);
				hi2 = X86F.Ssse3.shuffle_epi8(hi2, shuffleReverseByteOrder);
				hi3 = X86F.Ssse3.shuffle_epi8(hi3, shuffleReverseByteOrder);
			}

			int mask2,
				mask3;

			if (IsSse2Supported)
			{
				mask2 = (movemask_epi8(lo2) << 16) | movemask_epi8(hi2);
				mask3 = (movemask_epi8(lo3) << 16) | movemask_epi8(hi3);
			}
			else
			{
				mask2 = (X86F.Sse2.movemask_epi8(lo2) << 16) | X86F.Sse2.movemask_epi8(hi2);
				mask3 = (X86F.Sse2.movemask_epi8(lo3) << 16) | X86F.Sse2.movemask_epi8(hi3);
			}

			return (mask2, mask3);
		}

		/// <summary>
		///   Generates mesh geometry (vertices and triangles) for a single 2x2x2 voxel cube.
		/// </summary>
		[SkipLocalsInit]
		unsafe void MeshSamples(
			int* pos,
			float* samples,
			int edgeMask,
			bool flipTriangle,
			byte* materialsPtr
		)
		{
			const int r0 = (CHUNK_SIZE + 1) * (CHUNK_SIZE + 1);

			var r = stackalloc int[3] { r0, CHUNK_SIZE + 1, 1 };
			var bufferIndex = pos[2] + ((CHUNK_SIZE + 1) * pos[1]);

			if (pos[0] % 2 == 0)
			{
				bufferIndex += 1 + ((CHUNK_SIZE + 1) * (CHUNK_SIZE + 2));
			}
			else
			{
				r[0] = -r[0];
				bufferIndex += CHUNK_SIZE + 2;
			}

			buffer[bufferIndex] = vertices.Length;

			var vertexOffset = GetVertexPositionFromSamples(samples, edgeMask);
			var position = (new float3(pos[0], pos[1], pos[2]) + vertexOffset) * voxelSize;

			// Get material information
			var materialInfo = GetVertexMaterialInfo(pos, vertexOffset, materialsPtr);

			vertices.Add(
				new Vertex
				{
					position = position,
					normal = recalculateNormals
						? float3.zero
						: GetVertexNormalFromSamples(samples, voxelSize),
					color = materialInfo,
				}
			);

			// Store cell coordinates for fairing
			vertexCellCoords.Add(new int3(pos[0], pos[1], pos[2]));

			bounds.Item.Encapsulate(position);

			for (var i = 0; i < 3; i++)
			{
				if ((edgeMask & (1 << i)) == 0)
					continue;

				var iu = (i + 1) % 3;
				var iv = (i + 2) % 3;

				if (pos[iu] == 0 || pos[iv] == 0)
					continue;

				var du = r[iu];
				var dv = r[iv];

				indices.ResizeUninitialized(indices.Length + 6);
				var indicesPtr = indices.GetUnsafePtr() + indices.Length - 6;

				if (flipTriangle)
				{
					indicesPtr[0] = buffer[bufferIndex];
					indicesPtr[1] = buffer[bufferIndex - du - dv];
					indicesPtr[2] = buffer[bufferIndex - du];
					indicesPtr[3] = buffer[bufferIndex];
					indicesPtr[4] = buffer[bufferIndex - dv];
					indicesPtr[5] = buffer[bufferIndex - du - dv];
				}
				else
				{
					indicesPtr[0] = buffer[bufferIndex];
					indicesPtr[1] = buffer[bufferIndex - du - dv];
					indicesPtr[2] = buffer[bufferIndex - dv];
					indicesPtr[3] = buffer[bufferIndex];
					indicesPtr[4] = buffer[bufferIndex - du];
					indicesPtr[5] = buffer[bufferIndex - du - dv];
				}
			}
		}

		/// <summary>
		///   Determines vertex material by dominant non-air label within the 2x2x2 cell.
		///   Skips MATERIAL_AIR, breaks ties by nearest corner to the vertex.
		/// </summary>
		[SkipLocalsInit]
		unsafe Color32 GetVertexMaterialInfo(int* pos, float3 vertexOffset, byte* materialsPtr)
		{
			// Track up to 8 unique materials with counts and nearest-corner distance
			var uniqueMats = stackalloc byte[8];
			var counts = stackalloc int[8];
			var minDist = stackalloc float[8];
			for (var i = 0; i < 8; i++)
			{
				uniqueMats[i] = MATERIAL_AIR;
				counts[i] = 0;
				minDist[i] = float.MaxValue;
			}

			int uniqueCount = 0;

			for (var i = 0; i < 8; i++)
			{
				var corner = new float3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
				var cornerX = math.min(pos[0] + (int)corner.x, CHUNK_SIZE - 1);
				var cornerY = math.min(pos[1] + (int)corner.y, CHUNK_SIZE - 1);
				var cornerZ = math.min(pos[2] + (int)corner.z, CHUNK_SIZE - 1);
				var cornerIndex = (cornerX * CHUNK_SIZE * CHUNK_SIZE) + (cornerY * CHUNK_SIZE) + cornerZ;

				var mat = materialsPtr[cornerIndex];
				if (mat == MATERIAL_AIR)
					continue; // skip air

				var dist = length(corner - vertexOffset);

				// Find or insert material entry
				var found = false;
				for (var u = 0; u < uniqueCount; u++)
				{
					if (uniqueMats[u] == mat)
					{
						counts[u] += 1;
						if (dist < minDist[u])
							minDist[u] = dist;
						found = true;
						break;
					}
				}

				if (!found && uniqueCount < 8)
				{
					uniqueMats[uniqueCount] = mat;
					counts[uniqueCount] = 1;
					minDist[uniqueCount] = dist;
					uniqueCount++;
				}
			}

			byte selected = MATERIAL_AIR;
			if (uniqueCount > 0)
			{
				var bestIdx = 0;
				for (var u = 1; u < uniqueCount; u++)
				{
					if (
						counts[u] > counts[bestIdx]
						|| (counts[u] == counts[bestIdx] && minDist[u] < minDist[bestIdx])
					)
						bestIdx = u;
				}
				selected = uniqueMats[bestIdx];
			}

			return new Color32(selected, 0, 0, 255);
		}

		/// <summary>
		///   Applies surface fairing to smooth the mesh while preserving features.
		/// </summary>
		unsafe void ApplySurfaceFairing()
		{
			var vertexCount = vertices.Length;
			var tempPositions = new NativeArray<float3>(vertexCount, Allocator.Temp);

			try
			{
				var verticesPtr = vertices.GetUnsafePtr();

				for (var iteration = 0; iteration < fairingIterations; iteration++)
				{
					// Copy current positions
					for (var i = 0; i < vertexCount; i++)
						tempPositions[i] = verticesPtr[i].position;

					// Update each vertex
					for (var v = 0; v < vertexCount; v++)
						UpdateVertexPosition(v, tempPositions, verticesPtr);
				}
			}
			finally
			{
				tempPositions.Dispose();
			}
		}

		/// <summary>
		///   Updates a single vertex position using face neighbor averaging.
		/// </summary>
		unsafe void UpdateVertexPosition(
			int vertexIndex,
			NativeArray<float3> tempPositions,
			Vertex* verticesPtr
		)
		{
			var cellCoord = vertexCellCoords[vertexIndex];
			var currentPos = tempPositions[vertexIndex];

			// Calculate local average from face neighbors
			var localAverage = CalculateFaceNeighborAverage(vertexIndex, cellCoord, tempPositions);

			// Determine adaptive step size based on features
			var stepSize = GetAdaptiveStepSize(vertexIndex, verticesPtr);

			// Move toward average
			var newPos = currentPos + (stepSize * (localAverage - currentPos));

			// Constrain to cell with margin (scaled by voxel size)
			var scaledMargin = cellMargin * voxelSize;
			var cellMin = ((float3)cellCoord * voxelSize) + scaledMargin;
			var cellMax = (((float3)cellCoord + 1.0f) * voxelSize) - scaledMargin;
			newPos = clamp(newPos, cellMin, cellMax);

			verticesPtr[vertexIndex].position = newPos;
		}

		/// <summary>
		///   Calculates the average position of face-connected neighbors.
		/// </summary>
		unsafe float3 CalculateFaceNeighborAverage(
			int vertexIndex,
			int3 cellCoord,
			NativeArray<float3> positions
		)
		{
			var sum = float3.zero;
			var count = 0;

			// Check all 6 face directions
			var faceOffsets = stackalloc int3[6];
			faceOffsets[0] = new int3(-1, 0, 0); // -X
			faceOffsets[1] = new int3(1, 0, 0); // +X
			faceOffsets[2] = new int3(0, -1, 0); // -Y
			faceOffsets[3] = new int3(0, 1, 0); // +Y
			faceOffsets[4] = new int3(0, 0, -1); // -Z
			faceOffsets[5] = new int3(0, 0, 1); // +Z

			for (var dir = 0; dir < 6; dir++)
			{
				var neighborCell = cellCoord + faceOffsets[dir];
				var neighborVertex = FindVertexInCell(neighborCell);

				if (neighborVertex >= 0)
				{
					sum += positions[neighborVertex];
					count++;
				}
			}

			return count > 0 ? sum / count : positions[vertexIndex];
		}

		/// <summary>
		///   Finds a vertex in a specific cell (linear search for now).
		/// </summary>
		int FindVertexInCell(int3 targetCell)
		{
			for (var i = 0; i < vertexCellCoords.Length; i++)
				if (all(vertexCellCoords[i] == targetCell))
					return i;
			return -1;
		}

		/// <summary>
		///   Determines adaptive step size based on material boundaries and features.
		/// </summary>
		unsafe float GetAdaptiveStepSize(int vertexIndex, Vertex* verticesPtr)
		{
			var baseStepSize = fairingStepSize;

			// Check if near material boundary by examining neighbor materials
			if (IsNearMaterialBoundary(vertexIndex, verticesPtr))
				// Reduce step size significantly at material boundaries
				// to preserve sharp material transitions as per the paper
				baseStepSize *= 0.3f;

			// Further reduce at sharp features (check normal deviation)
			if (HasSharpFeature(vertexIndex, verticesPtr))
				baseStepSize *= 0.5f;

			return baseStepSize;
		}

		/// <summary>
		///   Checks if vertex is near a material boundary by examining neighbors.
		/// </summary>
		unsafe bool IsNearMaterialBoundary(int vertexIndex, Vertex* verticesPtr)
		{
			var myMaterial = verticesPtr[vertexIndex].color.r;
			var cellCoord = vertexCellCoords[vertexIndex];

			// Check all 6 face directions for different materials
			var faceOffsets = stackalloc int3[6];
			faceOffsets[0] = new int3(-1, 0, 0); // -X
			faceOffsets[1] = new int3(1, 0, 0); // +X
			faceOffsets[2] = new int3(0, -1, 0); // -Y
			faceOffsets[3] = new int3(0, 1, 0); // +Y
			faceOffsets[4] = new int3(0, 0, -1); // -Z
			faceOffsets[5] = new int3(0, 0, 1); // +Z

			for (var dir = 0; dir < 6; dir++)
			{
				var neighborCell = cellCoord + faceOffsets[dir];
				var neighborVertex = FindVertexInCell(neighborCell);

				if (neighborVertex >= 0)
				{
					var neighborMaterial = verticesPtr[neighborVertex].color.r;
					if (neighborMaterial != myMaterial)
						// Found a neighbor with different material
						return true;
				}
			}

			return false;
		}

		/// <summary>
		///   Detects sharp features by checking normal deviation among neighbors.
		/// </summary>
		unsafe bool HasSharpFeature(int vertexIndex, Vertex* verticesPtr)
		{
			if (!recalculateNormals)
				return false;

			var centerNormal = verticesPtr[vertexIndex].normal;
			var maxDot = 1.0f;

			var cellCoord = vertexCellCoords[vertexIndex];

			// Check all 6 face directions
			var faceOffsets = stackalloc int3[6];
			faceOffsets[0] = new int3(-1, 0, 0); // -X
			faceOffsets[1] = new int3(1, 0, 0); // +X
			faceOffsets[2] = new int3(0, -1, 0); // -Y
			faceOffsets[3] = new int3(0, 1, 0); // +Y
			faceOffsets[4] = new int3(0, 0, -1); // -Z
			faceOffsets[5] = new int3(0, 0, 1); // +Z

			for (var dir = 0; dir < 6; dir++)
			{
				var neighborCell = cellCoord + faceOffsets[dir];
				var neighborVertex = FindVertexInCell(neighborCell);

				if (neighborVertex >= 0)
				{
					var neighborNormal = verticesPtr[neighborVertex].normal;
					var dotProduct = dot(centerNormal, neighborNormal);
					maxDot = min(maxDot, dotProduct);
				}
			}

			// Sharp feature if normals differ significantly
			return maxDot < 0.7f; // ~45 degree threshold
		}

		/// <summary>
		///   Calculates the optimal vertex position within a 2x2x2 cube based on edge crossings.
		/// </summary>
		[SkipLocalsInit]
		static unsafe float3 GetVertexPositionFromSamples(float* samples, int edgeMask)
		{
			var vertPos = float3.zero;
			var edgeCrossings = 0;

			// Edge 0: (0,0,0) to (1,0,0)
			if ((edgeMask & 1) != 0)
			{
				var s0 = samples[0];
				var s1 = samples[1];
				var t = s0 / (s0 - s1);
				vertPos += new float3(t, 0, 0);
				++edgeCrossings;
			}

			// Edge 1: (0,0,0) to (0,1,0)
			if ((edgeMask & (1 << 1)) != 0)
			{
				var s0 = samples[0];
				var s1 = samples[2];
				var t = s0 / (s0 - s1);
				vertPos += new float3(0, t, 0);
				++edgeCrossings;
			}

			// Edge 2: (0,0,0) to (0,0,1)
			if ((edgeMask & (1 << 2)) != 0)
			{
				var s0 = samples[0];
				var s1 = samples[4];
				var t = s0 / (s0 - s1);
				vertPos += new float3(0, 0, t);
				++edgeCrossings;
			}

			// Edge 3: (1,0,0) to (1,1,0)
			if ((edgeMask & (1 << 3)) != 0)
			{
				var s0 = samples[1];
				var s1 = samples[3];
				var t = s0 / (s0 - s1);
				vertPos += new float3(1, t, 0);
				++edgeCrossings;
			}

			// Edge 4: (1,0,0) to (1,0,1)
			if ((edgeMask & (1 << 4)) != 0)
			{
				var s0 = samples[1];
				var s1 = samples[5];
				var t = s0 / (s0 - s1);
				vertPos += new float3(1, 0, t);
				++edgeCrossings;
			}

			// Edge 5: (0,1,0) to (1,1,0)
			if ((edgeMask & (1 << 5)) != 0)
			{
				var s0 = samples[2];
				var s1 = samples[3];
				var t = s0 / (s0 - s1);
				vertPos += new float3(t, 1, 0);
				++edgeCrossings;
			}

			// Edge 6: (0,1,0) to (0,1,1)
			if ((edgeMask & (1 << 6)) != 0)
			{
				var s0 = samples[2];
				var s1 = samples[6];
				var t = s0 / (s0 - s1);
				vertPos += new float3(0, 1, t);
				++edgeCrossings;
			}

			// Edge 7: (1,1,0) to (1,1,1)
			if ((edgeMask & (1 << 7)) != 0)
			{
				var s0 = samples[3];
				var s1 = samples[7];
				var t = s0 / (s0 - s1);
				vertPos += new float3(1, 1, t);
				++edgeCrossings;
			}

			// Edge 8: (0,0,1) to (1,0,1)
			if ((edgeMask & (1 << 8)) != 0)
			{
				var s0 = samples[4];
				var s1 = samples[5];
				var t = s0 / (s0 - s1);
				vertPos += new float3(t, 0, 1);
				++edgeCrossings;
			}

			// Edge 9: (0,0,1) to (0,1,1)
			if ((edgeMask & (1 << 9)) != 0)
			{
				var s0 = samples[4];
				var s1 = samples[6];
				var t = s0 / (s0 - s1);
				vertPos += new float3(0, t, 1);
				++edgeCrossings;
			}

			// Edge 10: (1,0,1) to (1,1,1)
			if ((edgeMask & (1 << 10)) != 0)
			{
				var s0 = samples[5];
				var s1 = samples[7];
				var t = s0 / (s0 - s1);
				vertPos += new float3(1, t, 1);
				++edgeCrossings;
			}

			// Edge 11: (0,1,1) to (1,1,1)
			if ((edgeMask & (1 << 11)) != 0)
			{
				var s0 = samples[6];
				var s1 = samples[7];
				var t = s0 / (s0 - s1);
				vertPos += new float3(t, 1, 1);
				++edgeCrossings;
			}

			return vertPos / edgeCrossings;
		}

		/// <summary>
		///   Estimates the surface normal at a vertex using the gradient of the voxel field.
		/// </summary>
		[SkipLocalsInit]
		static unsafe float3 GetVertexNormalFromSamples([NoAlias] float* samples, float voxelSize)
		{
			float3 normal;

			normal.z =
				samples[4]
				- samples[0]
				+ (samples[5] - samples[1])
				+ (samples[6] - samples[2])
				+ (samples[7] - samples[3]);

			normal.y =
				samples[2]
				- samples[0]
				+ (samples[3] - samples[1])
				+ (samples[6] - samples[4])
				+ (samples[7] - samples[5]);

			normal.x =
				samples[1]
				- samples[0]
				+ (samples[3] - samples[2])
				+ (samples[5] - samples[4])
				+ (samples[7] - samples[6]);

			// Scale normal by voxel size to maintain consistent behavior
			return normal * (-0.002f / voxelSize);
		}

		/// <summary>
		///   Recalculates vertex normals from triangle geometry for improved surface quality.
		/// </summary>
		[SkipLocalsInit]
		unsafe void RecalculateNormals()
		{
			var verticesPtr = vertices.GetUnsafePtr();
			var indicesPtr = indices.GetUnsafePtr();

			var indicesLength = indices.Length;

			for (var i = 0; i < indicesLength; i += 6)
			{
				var idx0 = indicesPtr[i + 0];
				var idx1 = indicesPtr[i + 1];
				var idx2 = indicesPtr[i + 2];
				var idx3 = indicesPtr[i + 4];

				var vert0 = verticesPtr[idx0];
				var vert1 = verticesPtr[idx1];
				var vert2 = verticesPtr[idx2];
				var vert3 = verticesPtr[idx3];

				var tangent0 = vert1.position - vert0.position;
				var tangent1 = vert2.position - vert0.position;
				var tangent2 = vert3.position - vert0.position;

				var triangleNormal0 = cross(tangent0, tangent1);
				var triangleNormal1 = cross(tangent2, tangent0);

				if (float.IsNaN(triangleNormal0.x))
					triangleNormal0 = float3.zero;
				if (float.IsNaN(triangleNormal1.x))
					triangleNormal1 = float3.zero;

				verticesPtr[idx0].normal = verticesPtr[idx0].normal + triangleNormal0 + triangleNormal1;
				verticesPtr[idx1].normal = verticesPtr[idx1].normal + triangleNormal0 + triangleNormal1;
				verticesPtr[idx2].normal = verticesPtr[idx2].normal + triangleNormal0;
				verticesPtr[idx3].normal = verticesPtr[idx3].normal + triangleNormal1;
			}

			var vertexCount = vertices.Length;
			for (var i = 0; i < vertexCount; i++)
			{
				var vertex = verticesPtr[i];
				var normalLength = length(vertex.normal);
				if (normalLength > 0.0001f)
				{
					vertex.normal = vertex.normal / normalLength;
					verticesPtr[i] = vertex;
				}
			}
		}
	}
}
