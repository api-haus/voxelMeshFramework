namespace Voxels.Core.Meshing.Budgets
{
	using Unity.Burst;
	using Unity.Entities;

	public struct MeshingBudgets : IComponentData
	{
		static readonly SharedStatic<MeshingBudgets> s_shared =
			SharedStatic<MeshingBudgets>.GetOrCreate<MeshingBudgets>();

		public static ref MeshingBudgets Current => ref s_shared.Data;

		public struct PerFrame
		{
			public int chunksCreated;
			public int proceduralSchedule;
			public int meshingSchedule;
			public int meshApplied;
			public int memoryAllocated;
		}

		public PerFrame perFrame;
		public bool async;

		public static readonly MeshingBudgets Realtime = new()
		{
			async = false,
			perFrame = new PerFrame
			{
				chunksCreated = 2,
				proceduralSchedule = 4,
				meshingSchedule = 4,
				meshApplied = 4,
				memoryAllocated = 8,
			},
		};

		public static readonly MeshingBudgets HeavyLoading = new()
		{
			async = true,
			perFrame = new PerFrame
			{
				chunksCreated = int.MaxValue,
				proceduralSchedule = 64,
				meshingSchedule = 64,
				meshApplied = 64,
				memoryAllocated = 64,
			},
		};

		public static readonly MeshingBudgets Unlimited = new()
		{
			async = true,
			perFrame = new PerFrame
			{
				chunksCreated = int.MaxValue,
				proceduralSchedule = int.MaxValue,
				meshingSchedule = int.MaxValue,
				meshApplied = int.MaxValue,
				memoryAllocated = int.MaxValue,
			},
		};
	}
}
