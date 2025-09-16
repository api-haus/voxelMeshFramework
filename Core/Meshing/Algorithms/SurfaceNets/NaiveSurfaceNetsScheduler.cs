namespace Voxels.Core.Meshing.Algorithms.SurfaceNets
{
	using Materials;
	using ThirdParty.SurfaceNets;
	using Unity.Burst;
	using Unity.Jobs;
	using static MaterialEncoding;

	/// <summary>
	///   Scheduler for the basic Surface Nets algorithm without materials or smoothing.
	///   This is the fastest meshing algorithm, suitable for collision meshes.
	/// </summary>
	[BurstCompile]
	public struct NaiveSurfaceNetsScheduler : IMeshingAlgorithmScheduler
	{
		public JobHandle Schedule(
			in MeshingInputData input,
			in MeshingOutputData output,
			JobHandle inputDeps
		)
		{
			var job = new NaiveSurfaceNets
			{
				edgeTable = input.edgeTable,
				volume = input.volume,
				materials = input.materials,
				buffer = output.buffer,
				indices = output.indices,
				vertices = output.vertices,
				bounds = output.bounds,
				normalsMode = input.normalsMode,
				voxelSize = input.voxelSize,
				positionJitter = input.positionJitter,
				materialEncoding = input.materialEncoding,
			};

			var meshingHandle = job.Schedule(inputDeps);

			// Material encoding pass scheduled after meshing
			switch (input.materialEncoding)
			{
				case NONE:
					return meshingHandle;
				case COLOR_VALUE_R:
					return new EncodeMaterialsValueRJob
					{
						materials = input.materials,
						vertices = output.vertices,
						voxelSize = input.voxelSize,
						chunkSize = input.chunkSize,
					}.Schedule(meshingHandle);
				case COLOR_PALETTE:
					return new EncodeMaterialsPaletteJob
					{
						materials = input.materials,
						vertices = output.vertices,
						voxelSize = input.voxelSize,
						chunkSize = input.chunkSize,
					}.Schedule(meshingHandle);
				case COLOR_SPLAT_4:
					return new EncodeMaterialsSplat4Job
					{
						materials = input.materials,
						vertices = output.vertices,
						voxelSize = input.voxelSize,
						chunkSize = input.chunkSize,
					}.Schedule(meshingHandle);
				case COLOR_SPLAT_8:
					return new EncodeMaterialsSplat8Job
					{
						materials = input.materials,
						vertices = output.vertices,
						voxelSize = input.voxelSize,
						chunkSize = input.chunkSize,
					}.Schedule(meshingHandle);
				case COLOR_SPLAT_12:
					return new EncodeMaterialsSplat12Job
					{
						materials = input.materials,
						vertices = output.vertices,
						voxelSize = input.voxelSize,
						chunkSize = input.chunkSize,
					}.Schedule(meshingHandle);
				case COLOR_SPLAT_16:
					return new EncodeMaterialsSplat16Job
					{
						materials = input.materials,
						vertices = output.vertices,
						voxelSize = input.voxelSize,
						chunkSize = input.chunkSize,
					}.Schedule(meshingHandle);
				default:
					return meshingHandle;
			}
		}
	}
}
