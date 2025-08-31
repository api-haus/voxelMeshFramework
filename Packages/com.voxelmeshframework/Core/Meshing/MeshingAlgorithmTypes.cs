namespace Voxels.Core.Meshing
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
		///   Surface Nets with optional fairing and material support.
		///   Configure fairing through enableFairing flag and related settings.
		/// </summary>
		[InspectorName("Naïve Surface Nets")]
		NAIVE_SURFACE_NETS = 0,

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
	/// </summary>
	public enum MaterialDistributionMode : byte
	{
		/// <summary>
		///   Assign discrete material ID from the nearest voxel corner (stored in color.r).
		/// </summary>
		[Obsolete("Deprecated: use BLENDED_RGBA_WEIGHTS or BLENDED_CORNER_SUM")]
		DISCRETE_NEAREST_CORNER = 0,

		/// <summary>
		///   Encode up to 4 material weights in RGBA channels via inverse distance weighting.
		/// </summary>
		BLENDED_RGBA_WEIGHTS = 1,

		/// <summary>
		///   Encode up to 4 material weights using corner-sum of the 8 cube corners (counts per material), normalized.
		/// </summary>
		BLENDED_CORNER_SUM = 2,
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
		///   Whether to recalculate normals from triangle geometry.
		/// </summary>
		public bool recalculateNormals;

		/// <summary>
		///   How to distribute materials to vertices.
		/// </summary>
		public MaterialDistributionMode materialDistributionMode;
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

		// Surface fairing parameters
		public bool enableFairing;
		public int fairingIterations;
		public float fairingStepSize;
		public float cellMargin;
		public bool recomputeNormalsAfterFairing;

		// Material distribution
		public MaterialDistributionMode materialDistributionMode;

		/// <summary>
		///   Default configuration for basic Surface Nets.
		/// </summary>
		public static VoxelMeshingAlgorithmComponent Default =>
			new()
			{
				algorithm = VoxelMeshingAlgorithm.NAIVE_SURFACE_NETS,
				enableFairing = false,
				fairingIterations = 5,
				fairingStepSize = 0.6f,
				cellMargin = 0.1f,
				recomputeNormalsAfterFairing = false,
				materialDistributionMode = MaterialDistributionMode.BLENDED_RGBA_WEIGHTS,
			};
	}
}
