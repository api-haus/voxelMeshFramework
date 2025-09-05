namespace Voxels.Core.Authoring
{
	using Unity.Logging;
	using Unity.Mathematics;
	using UnityEngine;
	using static Unity.Mathematics.math;
	using static VoxelConstants;
	using static VoxelEntityBridge;

	[DisallowMultipleComponent]
	[AddComponentMenu("Voxels/Rolling Grid Player Driver")]
	public sealed class RollingGridPlayerDriver : MonoBehaviour
	{
		[SerializeField]
		internal VoxelMeshGrid grid;

		// test hooks
		internal void __SetGrid(VoxelMeshGrid g) => grid = g;

		[SerializeField]
		bool autoEnableRolling = true;

		[SerializeField]
		// removed: single-axis stepping is no longer supported; the orchestrator handles multi-axis deltas progressively
		// bool singleAxisSteps = true;

		int3 lastSentAnchorChunk;
		bool hasLastSent;

		void Reset()
		{
			if (!grid)
				grid = FindFirstObjectByType<VoxelMeshGrid>();
			Log.Debug("[RGDriver] Reset: grid discovered={gridFound}", grid != null);
		}

		void Awake()
		{
			Log.Debug(
				"[RGDriver] Awake: autoEnableRolling={auto}, gridAssigned={hasGrid}",
				autoEnableRolling,
				grid != null
			);
			if (autoEnableRolling && grid)
			{
				Log.Debug("[RGDriver] Enabling rolling for grid {gid}", grid.gameObject.GetInstanceID());
				EnableRollingForGrid(grid.gameObject.GetInstanceID());
			}
		}

		void Update()
		{
			if (!grid)
				return;

			var stride = grid.voxelSize * EFFECTIVE_CHUNK_SIZE;
			var p = transform.position;
			var target = new int3(
				(int)floor(p.x / stride),
				(int)floor(p.y / stride),
				(int)floor(p.z / stride)
			);

			if (!hasLastSent)
			{
				Log.Debug(
					"[RGDriver] Initial send: pos={pos} stride={stride} targetAnchor={anchor}",
					p,
					stride,
					target
				);
				Send(target);
				lastSentAnchorChunk = target;
				hasLastSent = true;
				return;
			}

			if (all(target == lastSentAnchorChunk))
				return;

			// Always send the full target anchor; orchestrator will progress batches along dominant axis
			Log.Debug("[RGDriver] Direct send: from={from} to={to}", lastSentAnchorChunk, target);
			Send(target);
			lastSentAnchorChunk = target;
		}

		void Send(int3 anchorWorldChunk)
		{
			Log.Debug(
				"[RGDriver] Send move request: grid={gid} anchor={anchor}",
				grid.gameObject.GetInstanceID(),
				anchorWorldChunk
			);
			SendRollingMovementRequest(grid.gameObject.GetInstanceID(), anchorWorldChunk);
		}
	}
}
