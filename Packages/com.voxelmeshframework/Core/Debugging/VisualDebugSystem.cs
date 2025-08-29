#if ALINE && DEBUG
namespace Voxels.Core.Debugging
{
	using Unity.Entities;

	[UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
	public partial class VisualDebugBeginSystem : SystemBase
	{
		protected override void OnUpdate()
		{
			Visual.BeginFrame();
		}
	}

	[UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
	public partial class VisualDebugEndSystem : SystemBase
	{
		protected override void OnUpdate()
		{
			Visual.EndFrame();
		}
	}
}
#endif
