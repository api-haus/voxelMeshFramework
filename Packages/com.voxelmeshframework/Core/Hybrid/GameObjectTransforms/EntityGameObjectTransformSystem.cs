namespace Voxels.Core.Hybrid.GameObjectTransforms
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

			using (EntityGameObjectTransformSystem_UpdateLocalToWorld.Auto())
			{
				foreach (
					var (ltwRef, trs) in SystemAPI.Query<
						RefRW<LocalToWorld>,
						// ReSharper disable once Unity.Entities.MustBeSurroundedWithRefRwRo
						EntityFollowsGameObjectTransform
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
