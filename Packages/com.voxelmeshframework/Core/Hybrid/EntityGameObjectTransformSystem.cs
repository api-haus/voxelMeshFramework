namespace Voxels.Core.Hybrid
{
	using Unity.Entities;
	using Unity.Transforms;
	using static Unity.Mathematics.math;

	[RequireMatchingQueriesForUpdate]
	[UpdateBefore(typeof(TransformSystemGroup))]
	public partial class EntityGameObjectTransformSystem : SystemBase
	{
		protected override void OnUpdate()
		{
			foreach (
				var (ltwRef, trs) in SystemAPI.Query<
					RefRW<LocalTransform>,
					// ReSharper disable once Unity.Entities.MustBeSurroundedWithRefRwRo
					EntityGameObjectTransformAttachment
				>()
			)
			{
				ltwRef.ValueRW.Position = trs.attachTo.position;
				ltwRef.ValueRW.Rotation = trs.attachTo.rotation;
				ltwRef.ValueRW.Scale = cmax(trs.attachTo.localScale);
			}

			foreach (
				var (ltwRef, trs) in SystemAPI.Query<
					RefRW<LocalToWorld>,
					// ReSharper disable once Unity.Entities.MustBeSurroundedWithRefRwRo
					EntityGameObjectTransformAttachment
				>()
			)
				ltwRef.ValueRW.Value = trs.attachTo.localToWorldMatrix;
		}
	}
}
