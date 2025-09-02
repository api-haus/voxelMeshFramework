using NUnit.Framework;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;
using Voxels.Core;
using Voxels.Core.Concurrency;
using Voxels.Core.Meshing;
using Voxels.Core.Meshing.Tags;

namespace Voxels.Tests.Editor
{
	public class OrchestrationFenceTests
	{
		[Test]
		public void FenceRegistry_StoresTailAndTryComplete()
		{
			// Arrange
			var world = World.DefaultGameObjectInjectionWorld;
			if (world == null)
			{
				world = new World("Test");
				World.DefaultGameObjectInjectionWorld = world;
			}
			Assert.That(world, Is.Not.Null);
			var em = world.EntityManager;
			var e = em.CreateEntity(typeof(NativeVoxelMesh));

			VoxelJobFenceRegistry.Initialize();

			// No fence yet
			Assert.That(VoxelJobFenceRegistry.Tail(e).Equals(default(JobHandle)));
			Assert.That(VoxelJobFenceRegistry.TryComplete(e), Is.True);

			// Set a default tail
			var h = default(JobHandle);
			VoxelJobFenceRegistry.Update(e, h);
			Assert.That(VoxelJobFenceRegistry.TryComplete(e), Is.True);
		}
	}
}
