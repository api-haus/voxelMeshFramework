namespace Voxels.Core.Meshing.Algorithms
{
	using Unity.Jobs;

	/// <summary>
	///   Interface for scheduling meshing algorithms.
	///   Allows different algorithms to be swapped at runtime.
	/// </summary>
	public interface IMeshingAlgorithmScheduler
	{
		JobHandle Schedule(in MeshingInputData input, in MeshingOutputData output, JobHandle inputDeps);
	}
}
