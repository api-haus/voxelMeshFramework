namespace Voxels.Core.Meshing.Algorithms
{
	using System;
	using ThirdParty.SurfaceNets;
	using ThirdParty.SurfaceNets.Utils;
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Jobs;
	using Unity.Mathematics.Geometry;
	using UnityEngine;

	/// <summary>
	///   Available voxel meshing algorithms.
	///   Each algorithm has different performance and quality characteristics.
	/// </summary>
	public enum VoxelMeshingAlgorithm : byte
	{
		/// <summary>
		///   Surface Nets with material support.
		/// </summary>
		[InspectorName("Surface Nets")]
		NAIVE_SURFACE_NETS = 0,

		/// <summary>
		///   Surface Nets with fairing and material support.
		///   Configure fairing through related settings.
		/// </summary>
		[InspectorName("Surface Nets with Fairing (Sharp details)")]
		FAIRED_SURFACE_NETS = 1,

		/// <summary>
		///   Dual Contouring algorithm (future implementation).
		///   Preserves sharp features exactly using Hermite data.
		/// </summary>
		[InspectorName("Dual-Contouring")]
		DUAL_CONTOURING = 2,

		/// <summary>
		///   Classic Marching Cubes algorithm (future implementation).
		///   Simple but produces excessive triangles.
		/// </summary>
		[InspectorName("Marching Cubes")]
		MARCHING_CUBES = 3,
	}

	/// <summary>
	///   Controls how voxel materials are encoded per vertex.
	///   Single supported mode: corner-sum blended RGBA weights.
	/// </summary>
	public enum MaterialEncoding : byte
	{
		[InspectorName("No material encoding")]
		NONE = 0,

		/// <summary>
		///   Encode up to 4 material weights using corner-sum of the 8 cube corners (counts per material), normalized.
		/// </summary>
		[InspectorName("Direct color encoding from up to 256 material color palette")]
		COLOR_DIRECT = 1,

		/// <summary>
		///   Encode up to 4 material weights using corner-sum of the 8 cube corners (counts per material), normalized.
		/// </summary>
		[InspectorName("Splat-like color encoding for up to 4 materials")]
		COLOR_SPLAT_4 = 2,
	}

	/// <summary>
	///   Controls how vertex normals are populated during meshing.
	/// </summary>
	public enum NormalsMode : byte
	{
		/// <summary>
		///   Do not compute normals in the base meshing job. Useful when a later pass will recompute normals.
		/// </summary>
		[InspectorName("Do not compute normals")]
		NONE = 0,

		/// <summary>
		///   Compute normals from the voxel field gradient during vertex generation (fast, approximate).
		/// </summary>
		[InspectorName("Compute normals from SDF gradient")]
		GRADIENT = 1,

		/// <summary>
		///   Compute normals from triangle geometry after indices are produced (higher quality, slower).
		/// </summary>
		[InspectorName("Compute normals from triangle geometry")]
		TRIANGLE_GEOMETRY = 2,
	}

	/// <summary>
	///   Interface for scheduling meshing algorithms.
	///   Allows different algorithms to be swapped at runtime.
	/// </summary>
	public interface IMeshingAlgorithmScheduler
	{
		JobHandle Schedule(in MeshingInputData input, in MeshingOutputData output, JobHandle inputDeps);
	}

	/// <summary>
	///   Input data required for all meshing algorithms.
	/// </summary>
	[BurstCompile]
	public struct MeshingInputData
	{
		/// <summary>
		///   Signed distance field volume data.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeArray<sbyte> volume;

		/// <summary>
		///   Material IDs for each voxel (optional).
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeArray<byte> materials;

		/// <summary>
		///   Edge table for Surface Nets algorithms.
		/// </summary>
		[NoAlias]
		[ReadOnly]
		public NativeArray<ushort> edgeTable;

		/// <summary>
		///   Size of each voxel in world units.
		/// </summary>
		public float voxelSize;

		/// <summary>
		///   Size of the chunk (must be 32 for SIMD optimizations).
		/// </summary>
		public int chunkSize;

		/// <summary>
		///   Normal computation strategy for the base meshing job.
		/// </summary>
		public NormalsMode normalsMode;

		/// <summary>
		///   How to distribute materials to vertices.
		/// </summary>
		public MaterialEncoding materialEncoding;
	}

	/// <summary>
	///   Output data containers for meshing algorithms.
	/// </summary>
	[BurstCompile]
	public struct MeshingOutputData
	{
		/// <summary>
		///   Output vertex data.
		/// </summary>
		[NoAlias]
		public NativeList<Vertex> vertices;

		/// <summary>
		///   Output triangle indices.
		/// </summary>
		[NoAlias]
		public NativeList<int> indices;

		/// <summary>
		///   Temporary buffer for vertex connectivity.
		/// </summary>
		[NoAlias]
		public NativeArray<int> buffer;

		/// <summary>
		///   Output mesh bounds.
		/// </summary>
		[NoAlias]
		public UnsafePointer<MinMaxAABB> bounds;
	}

	/// <summary>
	///   Component that specifies which meshing algorithm to use.
	/// </summary>
	[Serializable]
	public struct VoxelMeshingAlgorithmComponent : IComponentData
	{
		public VoxelMeshingAlgorithm algorithm;
		public NormalsMode normalsMode;

		// Surface fairing parameters
		public int fairingIterations;
		public float fairingStepSize;
		public float cellMargin;
		public bool recomputeNormalsAfterFairing;

		// Seam handling
		public SeamConstraintMode seamConstraintMode;
		public float seamConstraintWeight; // 0..1 multiplier for step near seams
		public int seamBandWidth; // cells

		// Material distribution
		public MaterialEncoding materialEncoding;

		/// <summary>
		///   Default configuration for basic Surface Nets.
		/// </summary>
		public static VoxelMeshingAlgorithmComponent Default =>
			new()
			{
				algorithm = VoxelMeshingAlgorithm.NAIVE_SURFACE_NETS,
				fairingIterations = 5,
				fairingStepSize = 0.6f,
				cellMargin = 0.1f,
				normalsMode = NormalsMode.GRADIENT,
				recomputeNormalsAfterFairing = false,
				seamConstraintMode = SeamConstraintMode.SOFT_BAND,
				seamConstraintWeight = 0.5f,
				seamBandWidth = 2,
				materialEncoding = MaterialEncoding.COLOR_SPLAT_4,
			};
	}

	public enum SeamConstraintMode : byte
	{
		NONE = 0,
		FREEZE = 1,
		SOFT_BAND = 2,
	}
}
