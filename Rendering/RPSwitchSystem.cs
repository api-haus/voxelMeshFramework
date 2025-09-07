namespace Rendering
{
	using Unity.Burst;
	using Unity.Entities;
	using static Unity.Entities.WorldSystemFilterFlags;

	[WorldSystemFilter(Default | Editor)]
	public partial struct RPSwitchSystem : ISystem
	{
		[BurstDiscard]
		public void OnCreate(ref SystemState state)
		{
			GlobalRPKeyword.SetGlobalRPKeywords();
		}

		[BurstDiscard]
		public void OnUpdate(ref SystemState state) { }

		[BurstDiscard]
		public void OnDestroy(ref SystemState state) { }
	}
}
