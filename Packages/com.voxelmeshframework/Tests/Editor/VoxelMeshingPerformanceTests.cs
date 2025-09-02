using System.Collections;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;
using UnityEngine.TestTools;
using Voxels.Core.Authoring;
using Voxels.Core.Stamps;

namespace Voxels.Tests.Editor
{
	public class VoxelMeshingPerformanceTests
	{
		[UnityTest, Performance]
		public IEnumerator Measure_Frames_During_StampingAndMeshing()
		{
			var go = new GameObject("PerfVoxelMesh");
			go.AddComponent<VoxelMesh>();

			// Let systems set up the entity
			yield return null;
			yield return null;
			yield return null;

			var center = Vector3.zero;
			var radius = 1.5f;
			var stamp = new NativeVoxelStampProcedural
			{
				shape = new ProceduralShape
				{
					shape = ProceduralShape.Shape.SPHERE,
					sphere = new ProceduralSphere { center = center, radius = radius },
				},
				bounds = Unity.Mathematics.Geometry.MinMaxAABB.CreateFromCenterAndExtents(
					center,
					radius * 2f
				),
				strength = -0.5f,
				material = 1,
			};

			using (Measure.Frames().Scope("FrameTime.Stamp+Mesh"))
			{
				for (var i = 0; i < 60; i++)
				{
					VoxelAPI.Stamp(stamp);
					yield return null;
				}
			}

			Object.DestroyImmediate(go);
		}
	}
}
