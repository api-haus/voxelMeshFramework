// ReSharper disable InconsistentNaming

namespace Voxels.Core.Diagnostics
{
	using Unity.Profiling;

	public static class VoxelProfiler
	{
		public static class Marks
		{
			public static readonly ProfilerMarker VoxelMeshingSystem_Update = new(
				"Voxels/Meshing/SystemUpdate"
			);

			public static readonly ProfilerMarker VoxelMeshingSystem_Perform_Naive = new(
				"Voxels/Meshing/Perform/NaiveSurfaceNets"
			);

			public static readonly ProfilerMarker VoxelMeshingSystem_Perform_Fair = new(
				"Voxels/Meshing/Perform/FairSurfaceNets"
			);

			public static readonly ProfilerMarker VoxelMeshingSystem_Upload = new(
				"Voxels/Meshing/Schedule/UploadMeshJob"
			);

			public static readonly ProfilerMarker ManagedVoxelMeshingSystem_Update = new(
				"Voxels/ManagedMeshing/Update"
			);

			public static readonly ProfilerMarker ManagedVoxelMeshingSystem_ApplyMesh = new(
				"Voxels/ManagedMeshing/ApplyMesh"
			);

			public static readonly ProfilerMarker ManagedVoxelMeshingSystem_AttachMeshFilter = new(
				"Voxels/ManagedMeshing/AttachMeshFilter"
			);

			public static readonly ProfilerMarker ManagedVoxelMeshingSystem_AttachMeshCollider = new(
				"Voxels/ManagedMeshing/AttachMeshCollider"
			);

			public static readonly ProfilerMarker VoxelMeshAllocationSystem_Update = new(
				"Voxels/Allocation/Update"
			);

			public static readonly ProfilerMarker VoxelMeshAllocationSystem_Allocate = new(
				"Voxels/Allocation/Allocate"
			);

			public static readonly ProfilerMarker VoxelMeshAllocationSystem_Cleanup = new(
				"Voxels/Allocation/Cleanup"
			);

			public static readonly ProfilerMarker ProceduralVoxelGenerationSystem_Update = new(
				"Voxels/Procedural/Update"
			);

			public static readonly ProfilerMarker ProceduralVoxelGenerationSystem_Schedule = new(
				"Voxels/Procedural/Schedule"
			);

			public static readonly ProfilerMarker VoxelStampSystem_Update = new("Voxels/Stamps/Update");
			public static readonly ProfilerMarker VoxelStampSystem_Query = new("Voxels/Stamps/Query");

			public static readonly ProfilerMarker VoxelStampSystem_Schedule = new(
				"Voxels/Stamps/Schedule"
			);

			public static readonly ProfilerMarker VoxelSpatialSystem_Update = new(
				"Voxels/Spatial/Update"
			);

			public static readonly ProfilerMarker VoxelSpatialSystem_BuildHash = new(
				"Voxels/Spatial/BuildHash"
			);

			public static readonly ProfilerMarker VoxelSpatialSystem_Add = new("Voxels/Spatial/Add");
			public static readonly ProfilerMarker VoxelSpatialSystem_Query = new("Voxels/Spatial/Query");

			public static readonly ProfilerMarker EntityInstanceIDLifecycleSystem_Update = new(
				"Voxels/InstanceID/Update"
			);

			public static readonly ProfilerMarker EntityInstanceIDLifecycleSystem_MapUpdate = new(
				"Voxels/InstanceID/MapUpdate"
			);

			public static readonly ProfilerMarker EntityInstanceIDLifecycleSystem_Destroy = new(
				"Voxels/InstanceID/Destroy"
			);

			public static readonly ProfilerMarker EntityGameObjectTransformSystem_Update = new(
				"Voxels/Hybrid/TransformUpdate"
			);

			public static readonly ProfilerMarker EntityGameObjectTransformSystem_UpdateLTW = new(
				"Voxels/Hybrid/UpdateLocalTransform"
			);

			public static readonly ProfilerMarker EntityGameObjectTransformSystem_UpdateLocalToWorld =
				new("Voxels/Hybrid/UpdateLocalToWorld");

			public static readonly ProfilerMarker NativeVoxelMeshingProcessor_Schedule = new(
				"Voxels/Meshing/Processor/Schedule"
			);

			public static readonly ProfilerMarker NativeVoxelMeshingProcessor_Apply = new(
				"Voxels/Meshing/Processor/ApplyManaged"
			);

			public static readonly ProfilerMarker SharedStaticMeshingResources_Init = new(
				"Voxels/Shared/Init"
			);

			public static readonly ProfilerMarker SharedStaticMeshingResources_FillEdgeTable = new(
				"Voxels/Shared/FillEdgeTable"
			);

			public static readonly ProfilerMarker SharedStaticMeshingResources_Release = new(
				"Voxels/Shared/Release"
			);

			public static readonly ProfilerMarker SimpleNoiseVoxelGenerator_Schedule = new(
				"Voxels/Procedural/SimpleNoise/Schedule"
			);

			public static readonly ProfilerMarker VoxelEntityBridge_CreateMeshEntity = new(
				"Voxels/Bridge/CreateMeshEntity"
			);

			public static readonly ProfilerMarker VoxelEntityBridge_CreateGridEntity = new(
				"Voxels/Bridge/CreateGridEntity"
			);

			public static readonly ProfilerMarker VoxelEntityBridge_DestroyByInstanceID = new(
				"Voxels/Bridge/DestroyByInstanceID"
			);

			public static readonly ProfilerMarker VoxelEntityBridge_DestroyEntity = new(
				"Voxels/Bridge/DestroyEntity"
			);
		}
	}
}
