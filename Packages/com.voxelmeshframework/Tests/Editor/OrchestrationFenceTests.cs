namespace Voxels.Tests.Editor
{
	using Core.Concurrency;
	using NUnit.Framework;
	using Unity.Entities;
	using Unity.Jobs;

	public class OrchestrationFenceTests
	{
		[Test]
		public void FenceRegistry_StoresTailAndTryComplete()
		{
			// Arrange: initialize registry and use a synthetic entity key
			VoxelJobFenceRegistry.Initialize();
			var e = new Entity { Index = 1, Version = 1 };

			// No fence yet
			Assert.That(VoxelJobFenceRegistry.Tail(e).Equals(default));
			Assert.That(VoxelJobFenceRegistry.TryComplete(e), Is.True);

			// Set a default tail and verify completion clears it
			var h = default(JobHandle);
			VoxelJobFenceRegistry.UpdateFence(e, h);
			Assert.That(VoxelJobFenceRegistry.TryComplete(e), Is.True);
		}
	}
}
