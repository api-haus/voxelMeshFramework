namespace Voxels.Editor.Tests
{
	using Core.Spatial;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using static Core.VoxelConstants;

	[TestFixture]
	public class VoxelSpatialSystemTests
	{
		static SpatialVoxelObject MakeObject(float4x4 ltw, MinMaxAABB localBounds, float voxelSize)
		{
			return new SpatialVoxelObject
			{
				entity = new Entity { Index = 1, Version = 1 },
				localBounds = localBounds,
				voxelSize = voxelSize,
				ltw = ltw,
				wtl = math.inverse(ltw),
			};
		}

		[Test]
		public void SpatialHash_AddAndQuery_IdentityTransform()
		{
			var hash = new VoxelSpatialSystem.VoxelObjectHash
			{
				hash = new NativeParallelMultiHashMap<int3, SpatialVoxelObject>(1024, Allocator.Temp),
			};

			try
			{
				var localBounds = new MinMaxAABB(
					float3.zero,
					new float3(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE)
				);
				var obj = MakeObject(float4x4.identity, localBounds, 1f);
				hash.Add(obj);

				var query = new MinMaxAABB(new float3(1, 1, 1), new float3(5, 5, 5));
				using var results = hash.Query(query, Allocator.Temp);
				Assert.AreEqual(1, results.Length);
			}
			finally
			{
				hash.Dispose();
			}
		}

		[Test]
		public void SpatialHash_AddAndQuery_TranslatedTransform()
		{
			var hash = new VoxelSpatialSystem.VoxelObjectHash
			{
				hash = new NativeParallelMultiHashMap<int3, SpatialVoxelObject>(1024, Allocator.Temp),
			};

			try
			{
				var localBounds = new MinMaxAABB(
					float3.zero,
					new float3(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE)
				);
				var ltw = float4x4.TRS(
					new float3(EFFECTIVE_CHUNK_SIZE, 0, 0),
					quaternion.identity,
					new float3(1, 1, 1)
				);
				var obj = MakeObject(ltw, localBounds, 1f);
				hash.Add(obj);

				// Query in the translated region
				var query = new MinMaxAABB(new float3(31, 1, 1), new float3(35, 5, 5));
				using var results = hash.Query(query, Allocator.Temp);
				Assert.AreEqual(1, results.Length);
			}
			finally
			{
				hash.Dispose();
			}
		}

		[Test]
		public void SpatialHash_AddAndQuery_RotatedTransform()
		{
			var hash = new VoxelSpatialSystem.VoxelObjectHash
			{
				hash = new NativeParallelMultiHashMap<int3, SpatialVoxelObject>(1024, Allocator.Temp),
			};

			try
			{
				var localBounds = new MinMaxAABB(
					float3.zero,
					new float3(CHUNK_SIZE, CHUNK_SIZE, CHUNK_SIZE)
				);
				var rotation = quaternion.AxisAngle(new float3(0, 1, 0), math.radians(90));
				var ltw = float4x4.TRS(new float3(10, 0, 10), rotation, new float3(1, 1, 1));
				var obj = MakeObject(ltw, localBounds, 1f);
				hash.Add(obj);

				// Query a world AABB that should intersect the rotated volume
				var query = new MinMaxAABB(new float3(-5, 1, -5), new float3(20, 20, 20));
				using var results = hash.Query(query, Allocator.Temp);
				Assert.AreEqual(1, results.Length, "Rotated object should be found by query");
			}
			finally
			{
				hash.Dispose();
			}
		}
	}
}
