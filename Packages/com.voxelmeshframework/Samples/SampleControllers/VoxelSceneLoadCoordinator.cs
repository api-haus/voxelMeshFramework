using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;
using Voxels.Core.Grids;

namespace Voxels.Samples.SampleControllers
{
	public sealed class VoxelSceneLoadCoordinator : MonoBehaviour
	{
		[SerializeField]
		string sceneName;

		[SerializeField]
		int initialMeshesPerFrame = 10;

		float savedTimeScale;
		float savedFixedDelta;

		void Start()
		{
			savedTimeScale = Time.timeScale;
			savedFixedDelta = Time.fixedDeltaTime;
			StartCoroutine(LoadAndWait());
		}

		IEnumerator LoadAndWait()
		{
			Time.timeScale = 0f;
			Time.fixedDeltaTime = 0f;

			if (!string.IsNullOrEmpty(sceneName))
			{
				var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
				while (!op.isDone)
					yield return null;
			}

			// ensure ECS created grid entities
			yield return null;

			var world = World.DefaultGameObjectInjectionWorld;
			var em = world.EntityManager;

			// collect grids
			var grids = new List<Entity>();
			using (var q = em.CreateEntityQuery(typeof(NativeVoxelGrid)))
			{
				using var arr = q.ToEntityArray(Unity.Collections.Allocator.Temp);
				for (int i = 0; i < arr.Length; i++)
					grids.Add(arr[i]);
			}

			// increase budgets
			foreach (var g in grids)
				if (em.HasComponent<NativeVoxelGrid.MeshingBudget>(g))
					em.SetComponentData(
						g,
						new NativeVoxelGrid.MeshingBudget { maxMeshesPerFrame = initialMeshesPerFrame }
					);

			// wait for totals known
			bool TotalsKnown()
			{
				foreach (var g in grids)
				{
					if (!em.HasComponent<GridMeshingProgress>(g))
						return false;
					var p = em.GetComponentData<GridMeshingProgress>(g);
					if (p.totalChunks <= 0)
						return false;
				}
				return true;
			}
			while (!TotalsKnown())
				yield return null;

			bool AllDone()
			{
				foreach (var g in grids)
				{
					if (!em.IsComponentEnabled<NativeVoxelGrid.FullyMeshedEvent>(g))
						return false;
				}
				return true;
			}
			while (!AllDone())
				yield return null;

			Time.timeScale = savedTimeScale;
			Time.fixedDeltaTime = savedFixedDelta;
		}
	}
}
