namespace Voxels.Samples.SampleControllers
{
	using Core.Authoring;
	using Core.Hybrid;
	using UnityEngine;
	using UnityEngine.Events;

	public class AtlasLoadingHandler : MonoBehaviour
	{
		public UnityEvent onLoad;

		public VoxelMeshAtlas atlas;

		bool m_FinishedInitialLoad;

		void Update()
		{
			if (m_FinishedInitialLoad)
				return;
			if (!VoxelEntityBridge.TryGetAtlas(atlas, out var nativeAtlas))
				return;

			if (nativeAtlas.counters.pendingChunks.Value <= 0)
			{
				OnFinishLoading();
				m_FinishedInitialLoad = true;
				enabled = false;
			}
		}

		void OnEnable()
		{
			OnBeginLoading();
		}

		void OnBeginLoading()
		{
			Time.timeScale = 0;
		}

		void OnFinishLoading()
		{
			Time.timeScale = 1;
		}
	}
}
