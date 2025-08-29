namespace Voxels
{
	using Core.Stamps;
	using Unity.Entities;

	public static class VoxelAPI
	{
		static EntityManager EntityManager =>
			World.DefaultGameObjectInjectionWorld?.EntityManager ?? default;

		public static void Stamp(NativeVoxelStampProcedural stamp)
		{
			if (EntityManager.Equals(default))
				return;

			var ent = EntityManager.CreateEntity(typeof(NativeVoxelStampProcedural));

			EntityManager.SetComponentData(ent, stamp);
		}
	}
}
