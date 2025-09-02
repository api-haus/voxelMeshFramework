namespace Voxels.Core.ThirdParty.SurfaceNets
{
	using System;
	using Intrinsics;
	using Meshing;
	using Unity.Burst;
	using Unity.Burst.CompilerServices;
	using Unity.Collections;
	using Unity.Collections.LowLevel.Unsafe;
	using Unity.Jobs;
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
	using static VoxelConstants;
	using float3 = Unity.Mathematics.float3;
	using v128 = Unity.Burst.Intrinsics.v128;

	/// <summary>
	///   Fast Naive Surface Nets Implementation
	///   This is a high-performance voxel-to-mesh conversion algorithm that generates triangulated surfaces
	///   from 3D volume data (signed distance fields). The algorithm is optimized using SIMD instructions
	///   and processes voxels in groups to extract isosurfaces efficiently.
	///   Key Features:
	///   - SIMD-optimized processing using SSE2/SSE4.1/SSSE3 and ARM NEON intrinsics
	///   - Batch processing of voxel data with bit manipulation for early termination
	///   - Linear interpolation for smooth surface extraction
	///   - Support for normal calculation from voxel gradients or triangle-based recalculation
	///   Algorithm Overview:
	///   1. Process volume data in 2x2x2 voxel cubes
	///   2. Determine surface crossings using sign changes in voxel values
	///   3. Generate vertices at optimal positions within each cube
	///   4. Triangulate the surface using a lookup table approach
	///   5. Calculate normals either from voxel gradients or triangle geometry
	/// </summary>
	[BurstCompile(
		Debug = false,
		FloatMode = FloatMode.Fast,
		OptimizeFor = OptimizeFor.Performance,
		FloatPrecision = FloatPrecision.Low,
		DisableSafetyChecks = true,
		CompileSynchronously = true
	)]
	public struct NaiveSurfaceNets : IJob
	{
		/// <summary>
		///   Precomputed edge table that maps corner configurations to edge crossings.
		///   For each of the 256 possible corner sign combinations in a 2x2x2 cube,
		///   this table indicates which of the 12 edges have surface crossings.
		///   Each bit in the ushort value represents one edge.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeArray<ushort> edgeTable;

		public MaterialDistributionMode materialDistributionMode;

		/// <summary>
		///   3D volume data representing signed distance field values.
		///   Negative values indicate interior (solid), positive values indicate exterior (air).
		///   The algorithm finds surfaces where values cross zero (sign changes).
		///   Stored as sbyte for memory efficiency and SIMD processing optimization.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeArray<sbyte> volume;

		/// <summary>
		///   Material IDs for each voxel (0-255).
		///   Used to assign discrete material information to vertices.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeArray<byte> materials;

		/// <summary>
		///   Temporary buffer used for storing vertex indices during triangulation.
		///   This buffer maintains spatial relationships between adjacent vertices
		///   to enable proper triangle connectivity across voxel boundaries.
		/// </summary>
		[NoAlias]
		public NativeArray<int> buffer;

		/// <summary>
		///   Output triangle indices defining the mesh connectivity.
		///   Every three consecutive indices form one triangle.
		/// </summary>
		[NoAlias]
		public NativeList<int> indices;

		/// <summary>
		///   Output vertex data including positions and normals.
		///   Each vertex represents a point on the extracted isosurface.
		/// </summary>
		[NoAlias]
		public NativeList<Vertex> vertices;

		/// <summary>
		///   Bounding box that encompasses all generated vertices.
		///   Updated during vertex generation for culling and spatial queries.
		/// </summary>
		[NoAlias]
		public UnsafePointer<MinMaxAABB> bounds;

		/// <summary>
		///   Flag indicating whether to recalculate normals from triangle geometry
		///   or use the gradient-based normals calculated during vertex generation.
		///   Triangle-based normals are more accurate but computationally expensive.
		/// </summary>
		public bool recalculateNormals;

		public float voxelSize;

		/// <summary>
		///   Main execution entry point for the surface meshing algorithm.
		///   Validates requirements, initializes output containers, and orchestrates
		///   the complete voxel-to-mesh conversion process.
		/// </summary>
		public void Execute()
		{
			using var _ = VoxelMeshingSystem_Perform_Naive.Auto();

			// Validate that chunk size matches the SIMD optimization requirements
			// This algorithm is specifically optimized for 32x32x32 chunks
			if (CHUNK_SIZE != 32)
				throw new Exception("ChunkSize must be equal to 32 to use this job");

			// Initialize output containers and reset bounding box
			bounds.Item = new MinMaxAABB(float.PositiveInfinity, float.NegativeInfinity);
			indices.Clear();
			vertices.Clear();

			// Execute the main surface extraction algorithm
			ProcessVoxels();

			// Optionally recalculate normals from triangle geometry for higher quality
			if (recalculateNormals)
				RecalculateNormals();
		}

		/// <summary>
		///   Core voxel processing algorithm that extracts isosurfaces from volume data.
		///   This method implements a highly optimized surface nets algorithm using:
		///   - SIMD instructions for parallel processing of voxel rows
		///   - Bit manipulation for fast sign extraction and early termination
		///   - Data interleaving to improve cache performance
		///   - Batch processing of 2x2x2 voxel cubes
		///   The algorithm processes volume data by:
		///   1. Loading and interleaving voxel data for efficient SIMD access
		///   2. Extracting sign bits to identify surface crossings
		///   3. Using early termination to skip empty regions
		///   4. Generating vertices and triangles for surface-crossing cubes
		/// </summary>
		[SkipLocalsInit]
		unsafe void ProcessVoxels()
		{
			// ===== SAMPLE ARRAY SETUP =====
			// These arrays store interleaved voxel data for efficient SIMD processing.
			// Instead of accessing individual voxels, we load entire rows (32 voxels)
			// and interleave them with their X+1 neighbors for cache-friendly access.
			//
			// Memory layout visualization for a YZ slice:
			//   samples01: Contains voxels at current Y level and their X+1 neighbors interleaved
			//   samples23: Contains voxels at Y+1 level and their X+1 neighbors interleaved
			//
			//   Example interleaved pattern (showing Z indices):
			//   [0, 1024, 1, 1025, 2, 1026, 3, 1027, ...] where 1024 = X+1 offset
			//
			// This interleaving allows us to access both voxels needed for edge crossings
			// in a single memory access pattern, significantly improving performance.
			var samples01 = stackalloc sbyte[64]; // Current Y level voxels (interleaved with X+1)
			var samples23 = stackalloc sbyte[64]; // Next Y level voxels (interleaved with X+1)
			var matRows01 = stackalloc byte[64]; // Current Y level materials (interleaved with X+1)
			var matRows23 = stackalloc byte[64]; // Next Y level materials (interleaved with X+1)

			// Get direct pointer to volume data for high-performance SIMD access
			var volumePtr = (sbyte*)volume.GetUnsafeReadOnlyPtr();
			var materialsPtr = (byte*)materials.GetUnsafeReadOnlyPtr();

			// ===== SIGN BIT MASKS =====
			// These masks store the sign bits of voxel values for fast surface detection.
			// Each mask represents one row of 32 voxels along the Z-axis.
			// mask0/mask1: Sign bits for voxels at current Y level (X and X+1)
			// mask2/mask3: Sign bits for voxels at next Y level (X and X+1)
			// Negative voxel values set their corresponding bit, positive values clear it.
			int mask0 = 0, // Current Y, current X row sign bits
				mask1 = 0, // Current Y, next X row sign bits
				mask2 = 0, // Next Y, current X row sign bits
				mask3 = 0; // Next Y, next X row sign bits

			// ===== MAIN PROCESSING LOOPS =====
			// Process volume in 31x31x31 blocks (excluding boundary voxels)
			// Each iteration processes a column of 2x2x32 voxels
			for (var x = 0; x < CHUNK_SIZE_MINUS_ONE; x++)
			{
				// ===== INITIALIZE X COLUMN =====
				// Pre-calculate sign masks for the first Y level of the current X column.
				// This setup allows reuse of calculations as we iterate through Y levels.
				(mask2, mask3) = ExtractSignBitsAndSamples(
					volumePtr,
					samples23,
					materialsPtr,
					matRows23,
					x
				);

				// Process each Y level in the current X column
				for (var y = 0; y < CHUNK_SIZE_MINUS_ONE; y++)
				{
					// ===== SAMPLE ARRAY ROTATION =====
					// Efficiently reuse sample arrays by swapping pointers instead of copying data.
					// samples01 becomes the "current Y" level, samples23 becomes "next Y" level.
					// This avoids expensive memory copies while maintaining data locality.
					var temp = samples01;
					samples01 = samples23; // Previous "next Y" becomes current "current Y"
					samples23 = temp; // Previous "current Y" will be filled with new "next Y"

					var tempMat = matRows01;
					matRows01 = matRows23; // Mirror rotation for materials
					matRows23 = tempMat; // Will be filled with new "next Y"

					// ===== SIGN MASK ROTATION =====
					// Similarly reuse previously calculated sign masks for the current Y level.
					mask0 = mask2; // Previous "next Y, current X" becomes "current Y, current X"
					mask1 = mask3; // Previous "next Y, next X" becomes "current Y, next X"

					// Calculate new sign masks for the next Y level
					(mask2, mask3) = ExtractSignBitsAndSamples(
						volumePtr,
						samples23,
						materialsPtr,
						matRows23,
						x,
						y
					);

					// ===== SIMD MASK PREPARATION =====
					// Pack all four sign masks into a single SIMD vector for parallel processing.
					// This enables simultaneous testing of all four voxel rows (2x2 in XY, 32 in Z).
					var masks = new v128(mask0, mask1, mask2, mask3);

					// ===== EARLY TERMINATION TEST =====
					// Check if the entire 2x2x32 voxel column contains any surface crossings.
					// If all voxels have the same sign (all positive or all negative),
					// no surface exists in this region and we can skip expensive processing.
					//
					// The test_mix_ones_zeroes instruction returns 0 if all bits are the same,
					// non-zero if there's a mix of 0s and 1s (indicating sign changes).
					int zerosOnes;

					if (IsSse41Supported)
						zerosOnes = test_mix_ones_zeroes(masks, new v128(uint.MaxValue));
					else if (IsNeonSupported)
						zerosOnes = test_mix_ones_zeroesNEON(masks, new v128(uint.MaxValue));
					else
						zerosOnes = X86F.Sse4_1.test_mix_ones_zeroes(masks, new v128(uint.MaxValue));

					// Skip this entire column if no surface crossings exist
					if (zerosOnes == 0)
						continue;

					// ===== CORNER MASK EXTRACTION =====
					// Extract the high-order bit from each of the four 32-bit masks to create
					// a 4-bit corner mask for the first 2x2x2 cube. The shift left by 4 positions
					// these bits in the upper nibble, leaving space for the next Z-level bits.
					//
					// movemask_ps extracts the sign bit (bit 31) from each 32-bit element,
					// giving us the sign of the last voxel in each row (due to bit reversal).
					int cornerMask;

					if (IsSseSupported)
						cornerMask = movemask_ps(masks) << 4;
					else
						cornerMask = X86F.Sse.movemask_ps(masks) << 4;

					// Pre-allocate sample storage for the current 2x2x2 cube
					var samples = stackalloc float[8];

					// ===== Z-AXIS ITERATION =====
					// Process each 2x2x2 voxel cube along the Z-axis
					for (var z = 0; z < CHUNK_SIZE_MINUS_ONE; z++)
					{
						// ===== CORNER MASK PREPARATION =====
						// Right-shift to move the current Z-level corner bits to the lower nibble.
						// This gives us the complete 8-bit corner mask for the current 2x2x2 cube.
						cornerMask = cornerMask >> 4;

						// ===== BIT ADVANCEMENT =====
						// Left-shift all masks by 1 bit to expose the next voxel's sign bit
						// for the next iteration. This efficiently processes the Z-column
						// by advancing through the bit representations.
						if (IsSse2Supported)
							masks = slli_epi32(masks, 1);
						else if (IsNeonSupported)
							masks = vshlq_n_s32(masks, 1);
						else
							masks = X86F.Sse2.slli_epi32(masks, 1);

						// ===== NEXT CORNER MASK BITS =====
						// Extract the next set of sign bits and position them in the upper nibble
						// for the next iteration. This maintains the rolling window of corner masks.
						if (IsSseSupported)
							cornerMask |= movemask_ps(masks) << 4;
						else
							cornerMask |= X86F.Sse.movemask_ps(masks) << 4;

						// ===== EMPTY CUBE CHECK =====
						// Skip cubes where all 8 corners have the same sign (all 0s or all 1s).
						// No surface can exist within such cubes since there are no sign changes.
						if (cornerMask == 0 || cornerMask == 255)
							continue;

						// ===== EDGE CROSSING LOOKUP =====
						// Use the precomputed edge table to determine which of the 12 cube edges
						// have surface crossings. Each bit in the edge mask represents one edge.
						int edgeMask = edgeTable[cornerMask];

						// ===== SAMPLE EXTRACTION =====
						// Extract the 8 voxel values that form the current 2x2x2 cube.
						// The values are retrieved from the interleaved sample arrays using
						// the specific indexing pattern that matches the cube corner layout.
						//
						// Cube corner indexing (binary representation):
						//   0: (0,0,0)  4: (0,0,1)
						//   1: (1,0,0)  5: (1,0,1)
						//   2: (0,1,0)  6: (0,1,1)
						//   3: (1,1,0)  7: (1,1,1)
						var zz = z + z; // z * 2 for interleaved access
						samples[0] = samples01[zz + 0]; // (x, y, z)
						samples[1] = samples01[zz + 1]; // (x+1, y, z)
						samples[2] = samples23[zz + 0]; // (x, y+1, z)
						samples[3] = samples23[zz + 1]; // (x+1, y+1, z)
						samples[4] = samples01[zz + 2]; // (x, y, z+1)
						samples[5] = samples01[zz + 3]; // (x+1, y, z+1)
						samples[6] = samples23[zz + 2]; // (x, y+1, z+1)
						samples[7] = samples23[zz + 3]; // (x+1, y+1, z+1)

						// ===== COORDINATE SETUP =====
						// Store the cube's base coordinates for vertex positioning and indexing.
						// Using stack-allocated array for performance (faster than int3 for some reason).
						var pos = stackalloc int[3] { x, y, z };

						// ===== TRIANGLE ORIENTATION =====
						// Determine triangle winding order based on the first corner's sign.
						// This ensures consistent surface orientation and proper lighting.
						var flipTriangle = (cornerMask & 1) != 0;

						// ===== SURFACE MESHING =====
						// Generate vertex and triangle data for this cube
						MeshSamples(pos, samples, edgeMask, flipTriangle, matRows01, matRows23);
					}
				}
			}
		}

		/// <summary>
		///   Extracts sign bits from voxel data and loads interleaved samples for SIMD processing.
		///   This method performs several critical optimizations:
		///   1. Loads 32 voxels per row using SIMD instructions (16 bytes at a time)
		///   2. Interleaves voxels with their X+1 neighbors for cache-friendly access
		///   3. Reverses bit order to optimize subsequent mask operations
		///   4. Extracts sign bits into compact 32-bit masks for fast testing
		///   The interleaving pattern significantly improves performance by ensuring
		///   that when processing a 2x2x2 cube, all required voxel data is available
		///   in contiguous memory locations.
		/// </summary>
		/// <param name="volumePtr">Direct pointer to volume data for SIMD access</param>
		/// <param name="samples23">Output array for interleaved voxel samples</param>
		/// <param name="x">Current X coordinate in the chunk</param>
		/// <param name="y">Current Y coordinate (-1 for initial setup outside Y loop)</param>
		/// <returns>Tuple of sign bit masks for current X and X+1 voxel rows</returns>
		[SkipLocalsInit]
		unsafe (int, int) ExtractSignBitsAndSamples(
			sbyte* volumePtr,
			sbyte* samples23,
			byte* materialsPtr,
			byte* matRows23,
			int x,
			int y = -1 /* first case, outside Y loop */
		)
		{
			// ===== BIT REVERSAL PREPARATION =====
			// SIMD shuffle mask to reverse byte order within 16-byte vectors.
			// This reversal is crucial for correct bit extraction order in later steps.
			// The movemask instruction extracts from high to low bit positions,
			// but we need to process voxels in Z+ order (low to high positions).
			var shuffleReverseByteOrder = new v128(15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0);

			// ===== MEMORY ADDRESS CALCULATION =====
			// Calculate the base pointer for the current voxel row.
			// X_SHIFT and Y_SHIFT are bit shifts corresponding to chunk dimensions.
			// (y + 1) accounts for the Y loop starting at -1 for initialization.
			var ptr = volumePtr + (x << X_SHIFT) + ((y + 1) << Y_SHIFT);
			var mptr = materialsPtr + (x << X_SHIFT) + ((y + 1) << Y_SHIFT);

			// ===== SIMD DATA LOADING =====
			// Load voxel data in 16-byte chunks for parallel processing.
			// Each 16-byte load contains 16 consecutive voxels along the Z-axis.
			// We load both current X and X+1 rows to enable interleaving.
			//
			// Memory layout:
			//   lo2, hi2: First and second half of current X row (32 voxels total)
			//   lo3, hi3: First and second half of X+1 row (32 voxels total)
			//   +1024 offset corresponds to X+1 position in linearized volume
			v128 lo2; /* First 16 voxels at current X */
			v128 hi2; /* Next 16 voxels at current X */
			v128 lo3; /* First 16 voxels at X+1 */
			v128 hi3; /* Next 16 voxels at X+1 */

			v128 mlo2; /* First 16 materials at current X */
			v128 mhi2; /* Next 16 materials at current X */
			v128 mlo3; /* First 16 materials at X+1 */
			v128 mhi3; /* Next 16 materials at X+1 */

			if (IsSse2Supported)
			{
				lo2 = load_si128(ptr + 0);
				hi2 = load_si128(ptr + 16);
				lo3 = load_si128(ptr + 1024); // X+1 offset in volume
				hi3 = load_si128(ptr + 1040);

				mlo2 = load_si128((sbyte*)mptr + 0);
				mhi2 = load_si128((sbyte*)mptr + 16);
				mlo3 = load_si128((sbyte*)mptr + 1024);
				mhi3 = load_si128((sbyte*)mptr + 1040);
			}
			else if (IsNeonSupported)
			{
				lo2 = vld1q_u8((byte*)(ptr + 0));
				hi2 = vld1q_u8((byte*)(ptr + 16));
				lo3 = vld1q_u8((byte*)(ptr + 1024));
				hi3 = vld1q_u8((byte*)(ptr + 1040));

				mlo2 = vld1q_u8(mptr + 0);
				mhi2 = vld1q_u8(mptr + 16);
				mlo3 = vld1q_u8(mptr + 1024);
				mhi3 = vld1q_u8(mptr + 1040);
			}
			else
			{
				lo2 = X86F.Sse2.load_si128(ptr + 0);
				hi2 = X86F.Sse2.load_si128(ptr + 16);
				lo3 = X86F.Sse2.load_si128(ptr + 1024);
				hi3 = X86F.Sse2.load_si128(ptr + 1040);

				mlo2 = X86F.Sse2.load_si128((sbyte*)mptr + 0);
				mhi2 = X86F.Sse2.load_si128((sbyte*)mptr + 16);
				mlo3 = X86F.Sse2.load_si128((sbyte*)mptr + 1024);
				mhi3 = X86F.Sse2.load_si128((sbyte*)mptr + 1040);
			}

			// ===== VOXEL INTERLEAVING =====
			// Interleave voxels from current X and X+1 positions for efficient cube processing.
			// The unpacklo/hi instructions interleave bytes from two 16-byte vectors:
			//   unpacklo: Interleaves lower 8 bytes of each vector
			//   unpackhi: Interleaves upper 8 bytes of each vector
			//
			// Result pattern in samples23 array:
			//   [voxel(x,z=0), voxel(x+1,z=0), voxel(x,z=1), voxel(x+1,z=1), ...]
			//
			// This interleaving means that when processing 2x2x2 cubes, both X and X+1
			// voxel values are available in adjacent memory locations, eliminating
			// the need for separate array accesses and improving cache performance.
			if (IsSse2Supported)
			{
				store_si128(samples23 + 00, unpacklo_epi8(lo2, lo3));
				store_si128(samples23 + 16, unpackhi_epi8(lo2, lo3));
				store_si128(samples23 + 32, unpacklo_epi8(hi2, hi3));
				store_si128(samples23 + 48, unpackhi_epi8(hi2, hi3));

				store_si128((sbyte*)matRows23 + 00, unpacklo_epi8(mlo2, mlo3));
				store_si128((sbyte*)matRows23 + 16, unpackhi_epi8(mlo2, mlo3));
				store_si128((sbyte*)matRows23 + 32, unpacklo_epi8(mhi2, mhi3));
				store_si128((sbyte*)matRows23 + 48, unpackhi_epi8(mhi2, mhi3));
			}
			else
			{
				X86F.Sse2.store_si128(samples23 + 00, X86F.Sse2.unpacklo_epi8(lo2, lo3));
				X86F.Sse2.store_si128(samples23 + 16, X86F.Sse2.unpackhi_epi8(lo2, lo3));
				X86F.Sse2.store_si128(samples23 + 32, X86F.Sse2.unpacklo_epi8(hi2, hi3));
				X86F.Sse2.store_si128(samples23 + 48, X86F.Sse2.unpackhi_epi8(hi2, hi3));

				X86F.Sse2.store_si128((sbyte*)matRows23 + 00, X86F.Sse2.unpacklo_epi8(mlo2, mlo3));
				X86F.Sse2.store_si128((sbyte*)matRows23 + 16, X86F.Sse2.unpackhi_epi8(mlo2, mlo3));
				X86F.Sse2.store_si128((sbyte*)matRows23 + 32, X86F.Sse2.unpacklo_epi8(mhi2, mhi3));
				X86F.Sse2.store_si128((sbyte*)matRows23 + 48, X86F.Sse2.unpackhi_epi8(mhi2, mhi3));
			}

			// ===== BIT ORDER REVERSAL =====
			// Reverse the byte order within each 16-byte vector to optimize mask extraction.
			// This reversal ensures that when we extract sign bits using movemask_epi8,
			// the bits correspond to voxels in increasing Z order rather than decreasing.
			//
			// Without reversal: movemask extracts bits for Z positions [15,14,13,...,2,1,0]
			// With reversal: movemask extracts bits for Z positions [0,1,2,...,13,14,15]
			//
			// This ordering is crucial for the bit-shifting operations in the main loop.
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

			// ===== SIGN BIT EXTRACTION =====
			// Extract the sign bit (MSB) from each voxel value into compact 32-bit masks.
			// movemask_epi8 extracts the sign bit from each of 16 bytes into a 16-bit mask.
			// We combine the results from lo and hi vectors to create complete 32-bit masks.
			//
			// The bit positions in each mask correspond to Z coordinates:
			//   mask2: Sign bits for all 32 voxels at current X position
			//   mask3: Sign bits for all 32 voxels at X+1 position
			//
			// Sign bit interpretation:
			//   1 = Negative voxel value (inside surface)
			//   0 = Positive voxel value (outside surface)
			//
			// These masks enable extremely fast surface detection using bitwise operations.
			int mask2;
			int mask3;

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

			// Return the extracted sign masks for use in surface detection
			return (mask2, mask3);
		}

		/// <summary>
		///   Generates mesh geometry (vertices and triangles) for a single 2x2x2 voxel cube.
		///   This method implements the core surface extraction logic:
		///   1. Calculates optimal vertex position within the cube using edge crossings
		///   2. Estimates surface normal from voxel gradient
		///   3. Assigns discrete material ID using nearest-corner rule
		///   4. Generates triangle indices with proper connectivity to adjacent cubes
		///   5. Handles triangle orientation for consistent surface winding
		///   The algorithm uses a sophisticated indexing system to ensure that vertices
		///   are properly shared between adjacent cubes, creating a seamless mesh.
		/// </summary>
		/// <param name="pos">Base coordinates of the 2x2x2 cube in the volume</param>
		/// <param name="samples">8 voxel values at the cube corners</param>
		/// <param name="edgeMask">Bitmask indicating which edges have surface crossings</param>
		/// <param name="flipTriangle">Whether to flip triangle winding for proper orientation</param>
		/// <param name="materialsPtr">Direct pointer to material data for discrete assignment</param>
		[SkipLocalsInit]
		unsafe void MeshSamples(
			int* pos,
			float* samples,
			int edgeMask,
			bool flipTriangle,
			byte* matRows01,
			byte* matRows23
		)
		{
			// ===== BUFFER INDEXING SETUP =====
			// Calculate strides for 3D indexing in the vertex buffer.
			// The buffer stores vertex indices in a 3D grid matching the chunk structure.
			// This enables efficient lookup of previously created vertices for triangle connectivity.
			const int r0 = (CHUNK_SIZE + 1) * (CHUNK_SIZE + 1); // YZ plane stride

			var r = stackalloc int[3] { r0, CHUNK_SIZE + 1, 1 }; // [YZ stride, Z stride, unit stride]
			var bufferIndex = pos[2] + ((CHUNK_SIZE + 1) * pos[1]); // Base 2D index (Y*stride + Z)

			// ===== ALTERNATING PATTERN HANDLING =====
			// Handle the alternating vertex layout pattern used in Surface Nets.
			// This pattern ensures proper connectivity between adjacent cubes by
			// offsetting vertex positions in a checkerboard-like arrangement.
			//
			// Even X coordinates use positive offsets and normal stride directions.
			// Odd X coordinates use negative offsets and reversed stride directions.
			// This creates a tessellation pattern that avoids vertex duplication.
			if (pos[0] % 2 == 0)
			{
				// Even X: Standard layout with positive offsets
				bufferIndex += 1 + ((CHUNK_SIZE + 1) * (CHUNK_SIZE + 2));
			}
			else
			{
				// Odd X: Reversed layout with negative X stride
				r[0] = -r[0]; // Reverse YZ plane stride direction
				bufferIndex += CHUNK_SIZE + 2;
			}

			// ===== VERTEX CREATION =====
			// Store the current vertex index in the buffer for later triangle connectivity.
			// This index will be referenced by triangles in adjacent cubes.
			buffer[bufferIndex] = vertices.Length;

			// Calculate the optimal vertex position within the cube using edge crossings.
			// This creates smooth surfaces by positioning vertices at the weighted average
			// of all edge crossing points, rather than at cube centers.
			var vertexOffset = GetVertexPositionFromSamples(samples, edgeMask);
			var position = (new float3(pos[0], pos[1], pos[2]) + vertexOffset) * voxelSize;

			// ===== MATERIAL ASSIGNMENT =====
			// Discrete mode is deprecated; choose between blended modes.
			// Load 8 corner materials from interleaved material rows (mirrors samples indexing)
			var zzm = pos[2] << 1; // z * 2 for interleaved access
			var m0 = matRows01[zzm + 0];
			var m1 = matRows01[zzm + 1];
			var m2 = matRows23[zzm + 0];
			var m3 = matRows23[zzm + 1];
			var m4 = matRows01[zzm + 2];
			var m5 = matRows01[zzm + 3];
			var m6 = matRows23[zzm + 2];
			var m7 = matRows23[zzm + 3];

			var materialColor = GetVertexMaterialWeightsCornerSum_Interleaved(
				m0,
				m1,
				m2,
				m3,
				m4,
				m5,
				m6,
				m7
			);

			// Create and add the new vertex to the output list
			vertices.Add(
				new Vertex
				{
					position = position,
					// Choose normal calculation method based on flags
					normal = recalculateNormals
						? float3.zero
						: GetVertexNormalFromSamples(samples, voxelSize),
					// Encode materials per configuration
					color = materialColor,
				}
			);

			// Update the mesh bounding box to include the new vertex
			bounds.Item.Encapsulate(position);

			// ===== TRIANGLE GENERATION =====
			// Generate triangles for each of the three base component directions (X, Y, Z).
			// This loop creates quads (2 triangles) for surfaces that cross cube faces.
			// The triangulation follows the Surface Nets connectivity pattern.
			for (var i = 0; i < 3; i++)
			{
				// Check if this direction has an edge crossing (surface exists)
				if ((edgeMask & (1 << i)) == 0)
					continue;

				// Calculate the other two axes for triangle orientation
				var iu = (i + 1) % 3; // Next axis
				var iv = (i + 2) % 3; // Third axis

				// Skip boundary cases where we can't form complete triangles
				if (pos[iu] == 0 || pos[iv] == 0)
					continue;

				// ===== VERTEX INDEX CALCULATION =====
				// Calculate buffer offsets to find the four vertices that form a quad.
				// These vertices come from the current cube and three adjacent cubes.
				var du = r[iu]; // Offset in U direction
				var dv = r[iv]; // Offset in V direction

				// ===== TRIANGLE INDEX ALLOCATION =====
				// Efficiently allocate space for 6 indices (2 triangles) in one operation.
				// This batch allocation improves performance over individual Add() calls.
				indices.ResizeUninitialized(indices.Length + 6);
				var indicesPtr = indices.GetUnsafePtr() + indices.Length - 6;

				// ===== QUAD TRIANGULATION =====
				// Create two triangles from the four vertices forming a quad.
				// Triangle winding order is determined by the flipTriangle flag to ensure
				// consistent surface normals and proper lighting.
				//
				// Vertex arrangement for quad:
				//   bufferIndex - du - dv  ----  bufferIndex - dv
				//         |                           |
				//         |                           |
				//   bufferIndex - du       ----    bufferIndex
				//
				if (flipTriangle)
				{
					// Clockwise winding for inverted surfaces
					indicesPtr[0] = buffer[bufferIndex];
					indicesPtr[1] = buffer[bufferIndex - du - dv];
					indicesPtr[2] = buffer[bufferIndex - du];
					indicesPtr[3] = buffer[bufferIndex];
					indicesPtr[4] = buffer[bufferIndex - dv];
					indicesPtr[5] = buffer[bufferIndex - du - dv];
				}
				else
				{
					// Counter-clockwise winding for normal surfaces
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
		///   Calculates the optimal vertex position within a 2x2x2 cube based on edge crossings.
		///   This method implements the core mathematical principle of Surface Nets:
		///   Instead of placing vertices at cube centers, they are positioned at the
		///   weighted average of all edge crossing points. This creates smoother surfaces
		///   that better approximate the underlying implicit function.
		///   The algorithm:
		///   1. Checks each of the 12 cube edges for surface crossings (sign changes)
		///   2. For each crossing, calculates the exact intersection point using linear interpolation
		///   3. Averages all intersection points to determine the final vertex position
		///   Edge numbering follows the standard cube convention:
		///   - Edges 0-3: Bottom face edges
		///   - Edges 4-7: Vertical edges
		///   - Edges 8-11: Top face edges
		/// </summary>
		/// <param name="samples">8 voxel values at cube corners</param>
		/// <param name="edgeMask">Bitmask indicating which edges have crossings</param>
		/// <returns>Vertex position offset within the unit cube [0,1]³</returns>
		[SkipLocalsInit]
		static unsafe float3 GetVertexPositionFromSamples(float* samples, int edgeMask)
		{
			// Accumulator for averaging all edge crossing positions
			var vertPos = float3.zero;
			var edgeCrossings = 0;

			// ===== BOTTOM FACE EDGES (Z=0) =====

			// Edge 0: (0,0,0) to (1,0,0) - Bottom front edge
			if ((edgeMask & 1) != 0)
			{
				var s0 = samples[0]; // Corner (0,0,0)
				var s1 = samples[1]; // Corner (1,0,0)
				// Linear interpolation to find zero crossing point
				var t = s0 / (s0 - s1); // Interpolation parameter [0,1]
				vertPos += new float3(t, 0, 0); // Add X-direction crossing
				++edgeCrossings;
			}

			// Edge 1: (0,0,0) to (0,1,0) - Bottom left edge
			if ((edgeMask & (1 << 1)) != 0)
			{
				var s0 = samples[0]; // Corner (0,0,0)
				var s1 = samples[2]; // Corner (0,1,0)
				var t = s0 / (s0 - s1);
				vertPos += new float3(0, t, 0); // Add Y-direction crossing
				++edgeCrossings;
			}

			// Edge 2: (0,0,0) to (0,0,1) - Bottom vertical edge from front-left
			if ((edgeMask & (1 << 2)) != 0)
			{
				var s0 = samples[0]; // Corner (0,0,0)
				var s1 = samples[4]; // Corner (0,0,1)
				var t = s0 / (s0 - s1);
				vertPos += new float3(0, 0, t); // Add Z-direction crossing
				++edgeCrossings;
			}

			// Edge 3: (1,0,0) to (1,1,0) - Bottom right edge
			if ((edgeMask & (1 << 3)) != 0)
			{
				var s0 = samples[1]; // Corner (1,0,0)
				var s1 = samples[3]; // Corner (1,1,0)
				var t = s0 / (s0 - s1);
				vertPos += new float3(1, t, 0); // Add Y-direction crossing at X=1
				++edgeCrossings;
			}

			// Edge 4: (1,0,0) to (1,0,1) - Bottom vertical edge from front-right
			if ((edgeMask & (1 << 4)) != 0)
			{
				var s0 = samples[1]; // Corner (1,0,0)
				var s1 = samples[5]; // Corner (1,0,1)
				var t = s0 / (s0 - s1);
				vertPos += new float3(1, 0, t); // Add Z-direction crossing at X=1
				++edgeCrossings;
			}

			// Edge 5: (0,1,0) to (1,1,0) - Bottom back edge
			if ((edgeMask & (1 << 5)) != 0)
			{
				var s0 = samples[2]; // Corner (0,1,0)
				var s1 = samples[3]; // Corner (1,1,0)
				var t = s0 / (s0 - s1);
				vertPos += new float3(t, 1, 0); // Add X-direction crossing at Y=1
				++edgeCrossings;
			}

			// Edge 6: (0,1,0) to (0,1,1) - Bottom vertical edge from back-left
			if ((edgeMask & (1 << 6)) != 0)
			{
				var s0 = samples[2]; // Corner (0,1,0)
				var s1 = samples[6]; // Corner (0,1,1)
				var t = s0 / (s0 - s1);
				vertPos += new float3(0, 1, t); // Add Z-direction crossing at Y=1
				++edgeCrossings;
			}

			// Edge 7: (1,1,0) to (1,1,1) - Bottom vertical edge from back-right
			if ((edgeMask & (1 << 7)) != 0)
			{
				var s0 = samples[3]; // Corner (1,1,0)
				var s1 = samples[7]; // Corner (1,1,1)
				var t = s0 / (s0 - s1);
				vertPos += new float3(1, 1, t); // Add Z-direction crossing at X=1,Y=1
				++edgeCrossings;
			}

			// ===== TOP FACE EDGES (Z=1) =====

			// Edge 8: (0,0,1) to (1,0,1) - Top front edge
			if ((edgeMask & (1 << 8)) != 0)
			{
				var s0 = samples[4]; // Corner (0,0,1)
				var s1 = samples[5]; // Corner (1,0,1)
				var t = s0 / (s0 - s1);
				vertPos += new float3(t, 0, 1); // Add X-direction crossing at Z=1
				++edgeCrossings;
			}

			// Edge 9: (0,0,1) to (0,1,1) - Top left edge
			if ((edgeMask & (1 << 9)) != 0)
			{
				var s0 = samples[4]; // Corner (0,0,1)
				var s1 = samples[6]; // Corner (0,1,1)
				var t = s0 / (s0 - s1);
				vertPos += new float3(0, t, 1); // Add Y-direction crossing at Z=1
				++edgeCrossings;
			}

			// Edge 10: (1,0,1) to (1,1,1) - Top right edge
			if ((edgeMask & (1 << 10)) != 0)
			{
				var s0 = samples[5]; // Corner (1,0,1)
				var s1 = samples[7]; // Corner (1,1,1)
				var t = s0 / (s0 - s1);
				vertPos += new float3(1, t, 1); // Add Y-direction crossing at X=1,Z=1
				++edgeCrossings;
			}

			// Edge 11: (0,1,1) to (1,1,1) - Top back edge
			if ((edgeMask & (1 << 11)) != 0)
			{
				var s0 = samples[6]; // Corner (0,1,1)
				var s1 = samples[7]; // Corner (1,1,1)
				var t = s0 / (s0 - s1);
				vertPos += new float3(t, 1, 1); // Add X-direction crossing at Y=1,Z=1
				++edgeCrossings;
			}

			// ===== POSITION AVERAGING =====
			// Calculate the mean position of all edge crossings to determine
			// the optimal vertex placement within the cube. This averaging
			// creates smoother surfaces than simply using cube centers.
			return vertPos / edgeCrossings;
		}

		/// <summary>
		///   Estimates the surface normal at a vertex using the gradient of the voxel field.
		///   This method calculates the surface normal by approximating the gradient
		///   of the scalar field at the vertex position. The gradient points in the
		///   direction of steepest increase, so the negative gradient points toward
		///   the surface interior, giving us the outward-facing normal.
		///   The gradient is estimated using finite differences between opposite
		///   corners of the 2x2x2 cube. This provides a good approximation of
		///   the local surface orientation for lighting and shading calculations.
		/// </summary>
		/// <param name="samples">8 voxel values at cube corners</param>
		/// <returns>Estimated surface normal vector</returns>
		[SkipLocalsInit]
		static unsafe float3 GetVertexNormalFromSamples([NoAlias] float* samples, float voxelSize)
		{
			// ===== GRADIENT CALCULATION =====
			// Calculate partial derivatives using central differences across the cube.
			// Each component represents the rate of change in that direction.
			//
			// The finite difference approximation uses opposite cube corners:
			// ∂f/∂x ≈ (f(x+1) - f(x-1)) / 2Δx, where Δx = 1 for unit cube
			//
			// Cube corner layout:
			//   0:(0,0,0)  1:(1,0,0)  2:(0,1,0)  3:(1,1,0)
			//   4:(0,0,1)  5:(1,0,1)  6:(0,1,1)  7:(1,1,1)

			float3 normal;

			// Z-component: Difference between front and back faces
			// Compares all Z=1 corners against corresponding Z=0 corners
			normal.z =
				samples[4]
				- samples[0]
				+ // (0,0,1) - (0,0,0)
				(samples[5] - samples[1])
				+ // (1,0,1) - (1,0,0)
				(samples[6] - samples[2])
				+ // (0,1,1) - (0,1,0)
				(samples[7] - samples[3]); // (1,1,1) - (1,1,0)

			// Y-component: Difference between back and front faces
			// Compares all Y=1 corners against corresponding Y=0 corners
			normal.y =
				samples[2]
				- samples[0]
				+ // (0,1,0) - (0,0,0)
				(samples[3] - samples[1])
				+ // (1,1,0) - (1,0,0)
				(samples[6] - samples[4])
				+ // (0,1,1) - (0,0,1)
				(samples[7] - samples[5]); // (1,1,1) - (1,0,1)

			// X-component: Difference between right and left faces
			// Compares all X=1 corners against corresponding X=0 corners
			normal.x =
				samples[1]
				- samples[0]
				+ // (1,0,0) - (0,0,0)
				(samples[3] - samples[2])
				+ // (1,1,0) - (0,1,0)
				(samples[5] - samples[4])
				+ // (1,0,1) - (0,0,1)
				(samples[7] - samples[6]); // (1,1,1) - (0,1,1)

			// ===== NORMAL FINALIZATION =====
			// Scale and negate the gradient to get the outward-facing surface normal.
			// The negative sign ensures the normal points outward from the surface.
			// The scale factor accounts for the voxel value range (-127 to 127).
			return normal * (-0.002f / voxelSize); // Scale factor: -1/(127*4) approximately
		}

		/// <summary>
		///   Computes multi-material weights for a vertex using inverse distance weighting.
		///   Samples materials from all 8 corners of the voxel cube and calculates blend weights
		///   based on distance from vertex to each corner. Supports up to 4 materials with
		///   smooth interpolation for natural material transitions.
		/// </summary>
		/// <param name="pos">Base coordinates of the 2x2x2 cube in the volume</param>
		/// <param name="vertexOffset">Vertex position offset within the unit cube [0,1]³</param>
		/// <param name="materialsPtr">Direct pointer to material data</param>
		/// <returns>Color32 with material weights encoded in RGBA channels</returns>
		[SkipLocalsInit]
		static unsafe Color32 GetVertexMaterialWeights(
			int* pos,
			float3 vertexOffset,
			byte* materialsPtr
		)
		{
			// ===== CORNER MATERIAL SAMPLING =====
			// Sample materials from all 8 corners of the 2x2x2 cube
			var cornerMaterialsRaw = stackalloc byte[8];
			var cornerMaterialsMapped = stackalloc byte[8];
			var materialWeights = stackalloc float[4];

			for (var i = 0; i < 4; i++)
				materialWeights[i] = 0f;

			for (var i = 0; i < 8; i++)
			{
				var corner = new float3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
				var cornerX = min(pos[0] + (int)corner.x, CHUNK_SIZE - 1);
				var cornerY = min(pos[1] + (int)corner.y, CHUNK_SIZE - 1);
				var cornerZ = min(pos[2] + (int)corner.z, CHUNK_SIZE - 1);
				var cornerIndex = (cornerX * CHUNK_SIZE * CHUNK_SIZE) + (cornerY * CHUNK_SIZE) + cornerZ;

				var raw = materialsPtr[cornerIndex];
				cornerMaterialsRaw[i] = raw;
				cornerMaterialsMapped[i] = (byte)(raw % 4); // Limit to 4 materials
			}

			// ===== INVERSE DISTANCE WEIGHTING (skip AIR: raw==0) =====
			var totalWeight = 0f;

			for (var i = 0; i < 8; i++)
			{
				if (cornerMaterialsRaw[i] == MATERIAL_AIR)
					continue; // skip air

				var corner = new float3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
				var dist = length(corner - vertexOffset);
				var weight = 1f / (dist + 0.001f); // Avoid division by zero

				var mapped = cornerMaterialsMapped[i];
				materialWeights[mapped] += weight;
				totalWeight += weight;
			}

			// ===== NORMALIZATION AND ENCODING =====
			if (totalWeight > 0f)
				for (var i = 0; i < 4; i++)
					materialWeights[i] /= totalWeight;
			else
				// Fallback: pick first non-air corner if present
				for (var i = 0; i < 8; i++)
					if (cornerMaterialsRaw[i] != MATERIAL_AIR)
					{
						materialWeights[cornerMaterialsMapped[i]] = 1f;
						break;
					}

			return new Color32(
				(byte)(materialWeights[0] * 255f),
				(byte)(materialWeights[1] * 255f),
				(byte)(materialWeights[2] * 255f),
				(byte)(materialWeights[3] * 255f)
			);
		}

		/// <summary>
		///   Computes multi-material weights for a vertex using corner-sum (counts per material among the 8 cube corners),
		///   normalized.
		///   This matches the article's approach where per-corner contributions are summed without distance bias.
		/// </summary>
		/// <param name="pos">Base coordinates of the 2x2x2 cube in the volume</param>
		/// <param name="vertexOffset">Vertex position offset within the unit cube [0,1]³ (unused here)</param>
		/// <param name="materialsPtr">Direct pointer to material data</param>
		/// <returns>Color32 with material weights encoded in RGBA channels</returns>
		[SkipLocalsInit]
		static unsafe Color32 GetVertexMaterialWeightsCornerSum(
			int* pos,
			float3 vertexOffset,
			byte* materialsPtr
		)
		{
			var materialWeights = stackalloc float[4];
			for (var i = 0; i < 4; i++)
				materialWeights[i] = 0f;

			var contributing = 0f;
			for (var i = 0; i < 8; i++)
			{
				var corner = new float3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
				var cornerX = min(pos[0] + (int)corner.x, CHUNK_SIZE - 1);
				var cornerY = min(pos[1] + (int)corner.y, CHUNK_SIZE - 1);
				var cornerZ = min(pos[2] + (int)corner.z, CHUNK_SIZE - 1);
				var cornerIndex = (cornerX * CHUNK_SIZE * CHUNK_SIZE) + (cornerY * CHUNK_SIZE) + cornerZ;

				var raw = materialsPtr[cornerIndex];
				if (raw == MATERIAL_AIR)
					continue; // skip AIR
				var ch = (raw - 1) & 3; // 1->R, 2->G, 3->B, 4->A
				materialWeights[ch] += 1f;
				contributing += 1f;
			}

			if (contributing > 0f)
			{
				var inv = 1f / contributing;
				for (var i = 0; i < 4; i++)
					materialWeights[i] *= inv;
			}

			return new Color32(
				(byte)(materialWeights[0] * 255f),
				(byte)(materialWeights[1] * 255f),
				(byte)(materialWeights[2] * 255f),
				(byte)(materialWeights[3] * 255f)
			);
		}

		// SIMD-friendly variants that take 8 corner materials directly (already interleaved)
		//		[SkipLocalsInit]
		//		static Color32 GetVertexMaterialWeights_Interleaved(
		//			byte m0,
		//			byte m1,
		//			byte m2,
		//			byte m3,
		//			byte m4,
		//			byte m5,
		//			byte m6,
		//			byte m7,
		//			float3 vertexOffset
		//		)
		//		{
		//			// Removed: favor Corner-Sum mode as the single supported fast path
		//			return new Color32(0, 0, 0, 255);
		//		}

		[SkipLocalsInit]
		static Color32 GetVertexMaterialWeightsCornerSum_Interleaved(
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
			var w0 = 0f;
			var w1 = 0f;
			var w2 = 0f;
			var w3 = 0f;
			var count = 0f;

			void acc(byte mat)
			{
				if (mat == MATERIAL_AIR)
					return; // skip AIR
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
				var inv = 1f / count;
				w0 *= inv;
				w1 *= inv;
				w2 *= inv;
				w3 *= inv;
			}

			return new Color32(
				(byte)(w0 * 255f),
				(byte)(w1 * 255f),
				(byte)(w2 * 255f),
				(byte)(w3 * 255f)
			);
		}

		//		[SkipLocalsInit]
		//		static byte GetNearestCornerMaterial_Interleaved(
		//			byte m0,
		//			byte m1,
		//			byte m2,
		//			byte m3,
		//			byte m4,
		//			byte m5,
		//			byte m6,
		//			byte m7,
		//			float3 vertexOffset
		//		)
		//		{
		//			// Removed: discrete mode eliminated
		//			return MATERIAL_AIR;
		//		}

		[SkipLocalsInit]
		static unsafe byte GetNearestCornerMaterial(int* pos, float3 vertexOffset, byte* materialsPtr)
		{
			byte nearestMat = 0;
			var nearestDist = float.MaxValue;
			var foundNonAir = false;

			for (var i = 0; i < 8; i++)
			{
				var corner = new float3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
				var cornerX = min(pos[0] + (int)corner.x, CHUNK_SIZE - 1);
				var cornerY = min(pos[1] + (int)corner.y, CHUNK_SIZE - 1);
				var cornerZ = min(pos[2] + (int)corner.z, CHUNK_SIZE - 1);
				var cornerIndex = (cornerX * CHUNK_SIZE * CHUNK_SIZE) + (cornerY * CHUNK_SIZE) + cornerZ;

				var mat = materialsPtr[cornerIndex];
				if (mat == 0)
					continue; // skip AIR

				var dist = length(corner - vertexOffset);
				if (dist < nearestDist)
				{
					nearestDist = dist;
					nearestMat = mat;
					foundNonAir = true;
				}
			}

			return foundNonAir ? nearestMat : (byte)0;
		}

		/// <summary>
		///   Recalculates vertex normals from triangle geometry for improved surface quality.
		///   This method provides higher-quality normals by computing them directly from
		///   the triangle mesh geometry rather than from voxel gradients. Triangle-based
		///   normals are more accurate for lighting and shading, especially for surfaces
		///   with sharp features or when the voxel resolution is low.
		///   The algorithm processes triangles in pairs (each quad consists of 2 triangles)
		///   and accumulates normal contributions to shared vertices. This creates smooth
		///   shading across the surface while preserving sharp edges where appropriate.
		/// </summary>
		[SkipLocalsInit]
		unsafe void RecalculateNormals()
		{
			// Get direct pointers for high-performance access to vertex and index data
			var verticesPtr = vertices.GetUnsafePtr();
			var indicesPtr = indices.GetUnsafePtr();

			var indicesLength = indices.Length;

			// ===== TRIANGLE PAIR PROCESSING =====
			// Process triangles in groups of 6 indices (2 triangles forming a quad).
			// This grouping takes advantage of the fact that adjacent triangles
			// share vertices and can be processed more efficiently together.
			for (var i = 0; i < indicesLength; i += 6)
			{
				// ===== VERTEX INDEX EXTRACTION =====
				// Extract the 4 unique vertex indices from the 6 triangle indices.
				// Each quad uses 4 vertices arranged in 2 triangles with shared edges.
				//
				// Triangle layout for a quad:
				//   Triangle 1: [idx0, idx1, idx2]
				//   Triangle 2: [idx0, idx3, idx1] (sharing edge idx0-idx1)
				var idx0 = indicesPtr[i + 0]; // Shared vertex (appears in both triangles)
				var idx1 = indicesPtr[i + 1]; // Shared vertex (appears in both triangles)
				var idx2 = indicesPtr[i + 2]; // Unique to first triangle
				var idx3 = indicesPtr[i + 4]; // Unique to second triangle

				// ===== VERTEX POSITION RETRIEVAL =====
				// Get the actual vertex positions for normal calculation
				var vert0 = verticesPtr[idx0];
				var vert1 = verticesPtr[idx1];
				var vert2 = verticesPtr[idx2];
				var vert3 = verticesPtr[idx3];

				// ===== EDGE VECTOR CALCULATION =====
				// Calculate edge vectors from the shared vertex (vert0) to all other vertices.
				// These vectors define the triangle geometry and are used for cross products.
				var tangent0 = vert1.position - vert0.position; // Edge to second shared vertex
				var tangent1 = vert2.position - vert0.position; // Edge to first triangle's unique vertex
				var tangent2 = vert3.position - vert0.position; // Edge to second triangle's unique vertex

				// ===== TRIANGLE NORMAL CALCULATION =====
				// Calculate face normals using cross products of edge vectors.
				// The cross product gives a vector perpendicular to both input vectors,
				// with magnitude proportional to the triangle area (weighted normals).
				var triangleNormal0 = cross(tangent0, tangent1); // Normal for first triangle
				var triangleNormal1 = cross(tangent2, tangent0); // Normal for second triangle

				// ===== NaN PROTECTION =====
				// Handle degenerate triangles that can produce invalid normals.
				// Degenerate cases include zero-area triangles or numerical precision issues.
				if (float.IsNaN(triangleNormal0.x))
					triangleNormal0 = float3.zero;
				if (float.IsNaN(triangleNormal1.x))
					triangleNormal1 = float3.zero;

				// ===== NORMAL ACCUMULATION =====
				// Add triangle normals to vertex normals with proper weighting.
				// Shared vertices (idx0, idx1) receive contributions from both triangles,
				// while unique vertices (idx2, idx3) receive contributions from one triangle each.
				//
				// This accumulation creates smooth shading by averaging normals from
				// all adjacent triangles.
				verticesPtr[idx0].normal = verticesPtr[idx0].normal + triangleNormal0 + triangleNormal1;
				verticesPtr[idx1].normal = verticesPtr[idx1].normal + triangleNormal0 + triangleNormal1;
				verticesPtr[idx2].normal = verticesPtr[idx2].normal + triangleNormal0;
				verticesPtr[idx3].normal = verticesPtr[idx3].normal + triangleNormal1;
			}

			// ===== FINAL NORMALIZATION =====
			// Normalize all accumulated normals to ensure consistent lighting.
			// This fixes artifacts caused by varying normal magnitudes.
			var vertexCount = vertices.Length;
			for (var i = 0; i < vertexCount; i++)
			{
				var vertex = verticesPtr[i];
				var normalLength = length(vertex.normal);
				if (normalLength > 0.0001f) // Avoid division by zero
				{
					vertex.normal = vertex.normal / normalLength;
					verticesPtr[i] = vertex;
				}
			}
		}
	}
}
