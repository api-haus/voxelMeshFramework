namespace Voxels.Samples.SampleControllers
{
	using Core.Stamps;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using UnityEngine;
	using UnityEngine.InputSystem;
	using static Unity.Mathematics.math;

	[RequireComponent(typeof(Camera))]
	public class SampleFirstPersonDiggingCamera : MonoBehaviour
	{
		[SerializeField]
		InputActionReference digAction;

		[SerializeField]
		InputActionReference placeAction;

		[SerializeField]
		float radius = 1.5f;

		[SerializeField]
		[Range(0.01f, 1f)]
		float digStrength = 1;

		[SerializeField]
		[Range(0.01f, 1f)]
		float placeStrength = 1;

		[SerializeField]
		float maxDistance = 20f;

		[SerializeField]
		LayerMask layerMask = 1;

		[SerializeField]
		byte paintMaterial = 1;

		[SerializeField]
		int stampsPerSecond = 40;

		Camera m_Camera;

		bool m_DigPressed;
		double m_LastTime;
		bool m_PlacePressed;
		double m_TimeStep;

		void Awake()
		{
			OnValidate();
			digAction.action.Enable();
			placeAction.action.Enable();
			TryGetComponent(out m_Camera);
			m_LastTime = 0;
		}

		void Update()
		{
			m_DigPressed = digAction.action.IsPressed();
			m_PlacePressed = placeAction.action.IsPressed();

			DigUpdate();
		}

		void OnValidate()
		{
			m_TimeStep = rcp(stampsPerSecond);
		}

		void DigUpdate()
		{
			var time = Time.realtimeSinceStartupAsDouble;
			var dt = time - m_LastTime;
			var times = (int)floor(dt / m_TimeStep);

			if (times == 0)
				return;

			m_LastTime = time;

			for (var i = 0; i < times; i++)
				ApplyStamp();
		}

		void ApplyStamp()
		{
			var ray = m_Camera.ViewportPointToRay(Vector3.one * .5f);

			if (!Physics.Raycast(ray, out var hit, maxDistance, layerMask))
				return;

			float3 point = hit.point;

			var stamp = new NativeVoxelStampProcedural
			{
				shape = new ProceduralShape
				{
					shape = ProceduralShape.Shape.SPHERE,
					sphere = new ProceduralSphere
					{
						//
						center = point,
						radius = radius,
					},
				},
				bounds = MinMaxAABB.CreateFromCenterAndExtents(point, radius * 2f),
				material = paintMaterial,
			};

			if (m_DigPressed)
			{
				stamp.strength = -digStrength;
				VoxelAPI.Stamp(stamp);
			}
			else if (m_PlacePressed)
			{
				stamp.strength = placeStrength;
				VoxelAPI.Stamp(stamp);
			}
		}
	}
}
