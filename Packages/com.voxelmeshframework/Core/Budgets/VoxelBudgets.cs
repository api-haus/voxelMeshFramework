namespace Voxels.Core.Budgets
{
	using Unity.Burst;
	using Unity.Entities;

	public struct VoxelBudgets : IComponentData
	{
		static readonly SharedStatic<VoxelBudgets> s_shared =
			SharedStatic<VoxelBudgets>.GetOrCreate<VoxelBudgets>();

		public static ref VoxelBudgets Current => ref s_shared.Data;

		public struct PerFrame
		{
			public int chunksCreated;
			public int proceduralScheduled;
			public int reMeshScheduled;
			public int meshApplied;
			public int memoryAllocated;
		}

		public PerFrame perFrame;

		public static readonly VoxelBudgets Realtime = new()
		{
			perFrame = new PerFrame
			{
				chunksCreated = 2,
				proceduralScheduled = 4,
				reMeshScheduled = 4,
				meshApplied = 4,
				memoryAllocated = 8,
			},
		};

		public static readonly VoxelBudgets HeavyLoading = new()
		{
			perFrame = new PerFrame
			{
				chunksCreated = int.MaxValue,
				proceduralScheduled = 64,
				reMeshScheduled = 64,
				meshApplied = 64,
				memoryAllocated = 64,
			},
		};

		public static readonly VoxelBudgets Unlimited = new()
		{
			perFrame = new PerFrame
			{
				chunksCreated = int.MaxValue,
				proceduralScheduled = int.MaxValue,
				reMeshScheduled = int.MaxValue,
				meshApplied = int.MaxValue,
				memoryAllocated = int.MaxValue,
			},
		};
	}
}
