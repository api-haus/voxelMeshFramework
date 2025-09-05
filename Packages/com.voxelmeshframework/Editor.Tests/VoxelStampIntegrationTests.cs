namespace Voxels.Editor.Tests
{
	using Core.Meshing;
	using Core.Spatial;
	using Core.Stamps;
	using NUnit.Framework;
	using Unity.Collections;
	using Unity.Jobs;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using static Core.VoxelConstants;

	[TestFixture]
	public class VoxelStampIntegrationTests
	{
		[Test]
		public void StampAcrossAdjacentChunks_CopySharedOverlap_MakesSeamEqual()
		{
			var left = new VoxelVolumeData(Allocator.TempJob) { voxelSize = 1f };
			var right = new VoxelVolumeData(Allocator.TempJob) { voxelSize = 1f };
			try
			{
				var leftSvo = new SpatialVoxelObject
				{
					entity = default,
					localBounds = new MinMaxAABB(float3.zero, new float3(CHUNK_SIZE)),
					voxelSize = 1f,
					ltw = float4x4.identity,
					wtl = float4x4.identity,
				};
				var rightSvo = leftSvo;
				// Place right chunk at +EFFECTIVE_CHUNK_SIZE along X in world space
				rightSvo.ltw = float4x4.TRS(
					new float3(EFFECTIVE_CHUNK_SIZE, 0, 0),
					quaternion.identity,
					new float3(1, 1, 1)
				);
				rightSvo.wtl = math.inverse(rightSvo.ltw);

				// World-space stamp centered exactly on the seam between left and right chunks (x=ECS)
				var worldCenter = new float3(EFFECTIVE_CHUNK_SIZE, CHUNK_SIZE / 2, CHUNK_SIZE / 2);
				var stamp = new NativeVoxelStampProcedural
				{
					shape = new ProceduralShape
					{
						shape = ProceduralShape.Shape.SPHERE,
						sphere = new ProceduralSphere { center = worldCenter, radius = 4f },
					},
					bounds = new MinMaxAABB(worldCenter - 5, worldCenter + 5),
					strength = 1f,
					material = 1,
				};

				var leftNvm = new NativeVoxelMesh(Allocator.TempJob);
				leftNvm.volume = left;
				var leftParams = new StampScheduling.StampApplyParams
				{
					sdfScale = 32f,
					deltaTime = 0f,
					alphaPerSecond = 20f,
				};
				var leftStamp = StampScheduling.ScheduleApplyStamp(stamp, leftSvo, leftNvm, leftParams);
				leftStamp.Complete();

				var rightNvm = new NativeVoxelMesh(Allocator.TempJob);
				rightNvm.volume = right;
				var rightParams = new StampScheduling.StampApplyParams
				{
					sdfScale = 32f,
					deltaTime = 0f,
					alphaPerSecond = 20f,
				};
				var rightStamp = StampScheduling.ScheduleApplyStamp(stamp, rightSvo, rightNvm, rightParams);
				rightStamp.Complete();

				var copy = StampScheduling.ScheduleCopySharedOverlap(leftNvm, rightNvm, 0);
				copy.Complete();

				// Verify the shared 2-voxel strip matches: left high (x=30..31) to right low (x=0..1)
				for (var y = 0; y < CHUNK_SIZE; y++)
				for (var z = 0; z < CHUNK_SIZE; z++)
				for (var o = 0; o < CHUNK_OVERLAP; o++)
				{
					var lx = CHUNK_SIZE - CHUNK_OVERLAP + o; // 30,31
					var rx = 0 + o; // 0,1
					var lPtr = (lx << X_SHIFT) + (y << Y_SHIFT) + z;
					var rPtr = (rx << X_SHIFT) + (y << Y_SHIFT) + z;

					Assert.AreEqual(left.sdfVolume[lPtr], right.sdfVolume[rPtr]);
				}
			}
			finally
			{
				left.Dispose();
				right.Dispose();
			}
		}

		[Test]
		public void StampAcrossFourChunks_CopySharedOverlap_MakesBothSeamsEqual()
		{
			var a = new VoxelVolumeData(Allocator.TempJob) { voxelSize = 1f };
			var b = new VoxelVolumeData(Allocator.TempJob) { voxelSize = 1f };
			var c = new VoxelVolumeData(Allocator.TempJob) { voxelSize = 1f };
			var d = new VoxelVolumeData(Allocator.TempJob) { voxelSize = 1f };
			try
			{
				var aSvo = new SpatialVoxelObject
				{
					entity = default,
					localBounds = new MinMaxAABB(float3.zero, new float3(CHUNK_SIZE)),
					voxelSize = 1f,
					ltw = float4x4.identity,
					wtl = float4x4.identity,
				};
				var bSvo = aSvo;
				var cSvo = aSvo;
				var dSvo = aSvo;

				// Positions: A(0,0,0) B(+ECS,0,0) C(0,+ECS,0) D(+ECS,+ECS,0)
				bSvo.ltw = float4x4.TRS(
					new float3(EFFECTIVE_CHUNK_SIZE, 0, 0),
					quaternion.identity,
					new float3(1, 1, 1)
				);
				bSvo.wtl = math.inverse(bSvo.ltw);
				cSvo.ltw = float4x4.TRS(
					new float3(0, EFFECTIVE_CHUNK_SIZE, 0),
					quaternion.identity,
					new float3(1, 1, 1)
				);
				cSvo.wtl = math.inverse(cSvo.ltw);
				dSvo.ltw = float4x4.TRS(
					new float3(EFFECTIVE_CHUNK_SIZE, EFFECTIVE_CHUNK_SIZE, 0),
					quaternion.identity,
					new float3(1, 1, 1)
				);
				dSvo.wtl = math.inverse(dSvo.ltw);

				// Stamp at the central world point affecting all four
				var worldCenter = new float3(EFFECTIVE_CHUNK_SIZE, EFFECTIVE_CHUNK_SIZE, CHUNK_SIZE / 2);
				var stamp = new NativeVoxelStampProcedural
				{
					shape = new ProceduralShape
					{
						shape = ProceduralShape.Shape.SPHERE,
						sphere = new ProceduralSphere { center = worldCenter, radius = 6f },
					},
					bounds = new MinMaxAABB(worldCenter - 8, worldCenter + 8),
					strength = 1f,
					material = 2,
				};

				var nvmA = new NativeVoxelMesh(Allocator.TempJob);
				nvmA.volume = a;
				var pA = new StampScheduling.StampApplyParams
				{
					sdfScale = 32f,
					deltaTime = 0f,
					alphaPerSecond = 20f,
				};
				var hA = StampScheduling.ScheduleApplyStamp(stamp, aSvo, nvmA, pA);
				hA.Complete();
				var nvmB = new NativeVoxelMesh(Allocator.TempJob);
				nvmB.volume = b;
				var pB = new StampScheduling.StampApplyParams
				{
					sdfScale = 32f,
					deltaTime = 0f,
					alphaPerSecond = 20f,
				};
				var hB = StampScheduling.ScheduleApplyStamp(stamp, bSvo, nvmB, pB);
				hB.Complete();
				var nvmC = new NativeVoxelMesh(Allocator.TempJob);
				nvmC.volume = c;
				var pC = new StampScheduling.StampApplyParams
				{
					sdfScale = 32f,
					deltaTime = 0f,
					alphaPerSecond = 20f,
				};
				var hC = StampScheduling.ScheduleApplyStamp(stamp, cSvo, nvmC, pC);
				hC.Complete();
				var nvmD = new NativeVoxelMesh(Allocator.TempJob);
				nvmD.volume = d;
				var pD = new StampScheduling.StampApplyParams
				{
					sdfScale = 32f,
					deltaTime = 0f,
					alphaPerSecond = 20f,
				};
				var hD = StampScheduling.ScheduleApplyStamp(stamp, dSvo, nvmD, pD);
				hD.Complete();

				var hAB = StampScheduling.ScheduleCopySharedOverlap(nvmA, nvmB, 0);
				hAB.Complete();
				var hCD = StampScheduling.ScheduleCopySharedOverlap(nvmC, nvmD, 0);
				hCD.Complete();
				var hAC = StampScheduling.ScheduleCopySharedOverlap(nvmA, nvmC, 1);
				hAC.Complete();
				var hBD = StampScheduling.ScheduleCopySharedOverlap(nvmB, nvmD, 1);
				hBD.Complete();

				// Check X seam rows A->B and C->D
				for (var y = 0; y < CHUNK_SIZE; y++)
				for (var z = 0; z < CHUNK_SIZE; z++)
				for (var o = 0; o < CHUNK_OVERLAP; o++)
				{
					var sx = CHUNK_SIZE - CHUNK_OVERLAP + o;
					var dx = 0 + o;
					var aPtr = (sx << X_SHIFT) + (y << Y_SHIFT) + z;
					var bPtr = (dx << X_SHIFT) + (y << Y_SHIFT) + z;
					var cPtr = aPtr;
					var dPtr = bPtr;
					Assert.AreEqual(a.sdfVolume[aPtr], b.sdfVolume[bPtr]);
					Assert.AreEqual(c.sdfVolume[cPtr], d.sdfVolume[dPtr]);
				}

				// Check Y seam columns A->C and B->D
				for (var x = 0; x < CHUNK_SIZE; x++)
				for (var z = 0; z < CHUNK_SIZE; z++)
				for (var o = 0; o < CHUNK_OVERLAP; o++)
				{
					var sy = CHUNK_SIZE - CHUNK_OVERLAP + o;
					var dy = 0 + o;
					var aPtr = (x << X_SHIFT) + (sy << Y_SHIFT) + z;
					var cPtr = (x << X_SHIFT) + (dy << Y_SHIFT) + z;
					var bPtr = aPtr;
					var dPtr = cPtr;
					Assert.AreEqual(a.sdfVolume[aPtr], c.sdfVolume[cPtr]);
					Assert.AreEqual(b.sdfVolume[bPtr], d.sdfVolume[dPtr]);
				}
			}
			finally
			{
				a.Dispose();
				b.Dispose();
				c.Dispose();
				d.Dispose();
			}
		}

		public void QueryThenApply_ModifiesIntersectingVolume()
		{
			// Build a spatial object with identity transform and allocated data
			var volume = new VoxelVolumeData(Allocator.TempJob) { voxelSize = 1f };
			try
			{
				var spatial = new SpatialVoxelObject
				{
					entity = default,
					localBounds = new MinMaxAABB(float3.zero, new float3(CHUNK_SIZE)),
					voxelSize = 1f,
					ltw = float4x4.identity,
					wtl = float4x4.identity,
				};

				var sh = new VoxelSpatialSystem.VoxelObjectHash
				{
					hash = new NativeParallelMultiHashMap<int3, SpatialVoxelObject>(64, Allocator.Temp),
				};

				try
				{
					sh.Add(spatial);

					// Issue a world-space stamp centered at local center (world==local here)
					var stamp = new NativeVoxelStampProcedural
					{
						shape = new ProceduralShape
						{
							shape = ProceduralShape.Shape.SPHERE,
							sphere = new ProceduralSphere { center = new float3(16, 16, 16), radius = 4f },
						},
						bounds = new MinMaxAABB(new float3(12, 12, 12), new float3(20, 20, 20)),
						strength = 1f,
						material = 5,
					};

					using var candidates = sh.Query(stamp.bounds, Allocator.Temp);
					Assert.AreEqual(1, candidates.Length);

					var cand = candidates[0];
					var sdf = volume.sdfVolume;
					var mat = volume.materials;

					var job = new ApplyVoxelStampJob
					{
						stamp = stamp,
						volumeSdf = sdf,
						volumeMaterials = mat,
						localVolumeBounds = cand.localBounds,
						voxelSize = cand.voxelSize,
						volumeLTW = cand.ltw,
						volumeWTL = cand.wtl,
						sdfScale = 32f,
					};

					job.Schedule().Complete();

					var idx = (16 << X_SHIFT) + (16 << Y_SHIFT) + 16;
					Assert.GreaterOrEqual(sdf[idx], 96);
					Assert.AreEqual(5, mat[idx]);
				}
				finally
				{
					sh.Dispose();
				}
			}
			finally
			{
				volume.Dispose();
			}
		}
	}
}
