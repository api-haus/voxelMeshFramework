namespace Voxels.Core.Grids
{
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Jobs;

	public struct NativeGridMeshingCounters : ICleanupComponentData, INativeDisposable
	{
		public NativeReference<int> inFlight;

		public void Dispose()
		{
			if (inFlight.IsCreated)
				inFlight.Dispose();
		}

		public JobHandle Dispose(JobHandle inputDeps)
		{
			if (inFlight.IsCreated)
				return inFlight.Dispose(inputDeps);
			return inputDeps;
		}
	}
}
