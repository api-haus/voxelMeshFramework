namespace Voxels
{
	using Core;
	using Core.Stamps;

	public static class VoxelAPI
	{
		public static void Stamp(NativeVoxelStampProcedural stamp)
		{
			if (!VoxelEntityBridge.TryGetEntityManager(out var em))
				return;

			var ent = em.CreateEntity(typeof(NativeVoxelStampProcedural));

			em.SetComponentData(ent, stamp);
		}
	}
}
