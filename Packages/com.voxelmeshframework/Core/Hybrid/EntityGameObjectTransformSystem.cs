namespace Voxels.Core.Hybrid
{
	using Unity.Entities;
	using Unity.Transforms;
	using static Diagnostics.VoxelProfiler.Marks;

	[RequireMatchingQueriesForUpdate]
	[UpdateBefore(typeof(TransformSystemGroup))]
	public partial class EntityGameObjectTransformSystem : SystemBase
	{
		protected override void OnUpdate()
		{
			using var _ = EntityGameObjectTransformSystem_Update.Auto();

			// using (EntityGameObjectTransformSystem_UpdateLocalTransform.Auto())
			// {
			// 	foreach (
			// 		var (ltwRef, trs) in SystemAPI.Query<
			// 			RefRW<LocalTransform>,
			// 			// ReSharper disable once Unity.Entities.MustBeSurroundedWithRefRwRo
			// 			EntityGameObjectTransformAttachment
			// 		>()
			// 	)
			// 	{
			// 		if (!trs.attachTo)
			// 			continue;
			// 		ltwRef.ValueRW.Position = trs.attachTo.position;
			// 		ltwRef.ValueRW.Rotation = trs.attachTo.rotation;
			// 		ltwRef.ValueRW.Scale = cmax(trs.attachTo.localScale);
			// 	}
			// }

			using (EntityGameObjectTransformSystem_UpdateLocalToWorld.Auto())
			{
				foreach (
					var (ltwRef, trs) in SystemAPI.Query<
						RefRW<LocalToWorld>,
						// ReSharper disable once Unity.Entities.MustBeSurroundedWithRefRwRo
						EntityGameObjectTransformAttachment
					>()
				)
				{
					if (null == trs || !trs.attachTo)
						continue;
					ltwRef.ValueRW.Value = trs.attachTo.localToWorldMatrix;
				}
			}
		}
	}
}
