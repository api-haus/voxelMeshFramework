namespace Voxels.Core.Debugging
{
	using Authoring;
	using Unity.Burst;
#if ALINE
	using Drawing;
#endif

	public static class Visual
	{
		static readonly SharedStatic<Shared> s_shared = SharedStatic<Shared>.GetOrCreate<Shared>();

#if ALINE
		public static ref CommandBuilder Draw => ref s_shared.Data.builder;
#endif

		public static void BeginFrame()
		{
			s_shared.Data.Initialize();
		}

		public static void EndFrame()
		{
			s_shared.Data.Dispose();
		}

		struct Shared
		{
#if ALINE
			public CommandBuilder builder;

			public void Initialize()
			{
				builder = DrawingManager.GetBuilder(true);
			}

			public void Dispose()
			{
				if (VoxelDebugging.IsEnabled)
					builder.Dispose();
				else
					builder.DiscardAndDispose();
			}
#else
			public void Initialize() { }

			public void Dispose() { }
#endif
		}

#if ALINE
#endif
	}
}
