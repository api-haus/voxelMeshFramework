namespace Voxels.Samples.SampleControllers
{
	using Core.Stamps;
	using Unity.Mathematics;
	using Unity.Mathematics.Geometry;
	using UnityEngine;
	using UnityEngine.InputSystem;

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
		[Range(1, 127)]
		int digStrength = 1;

		[SerializeField]
		[Range(1, 127)]
		int placeStrength = 1;

		[SerializeField]
		float maxDistance = 20f;

		[SerializeField]
		LayerMask layerMask = 1;

		[SerializeField]
		byte paintMaterial = 1;

		[SerializeField]
		Core.Stamps.StampScheduling.StampApplyParams applyParams = new()
		{
			sdfScale = 32f,
			deltaTime = 0f,
			alphaPerSecond = 20f,
		};

		readonly RaycastHit[] m_Hits = new RaycastHit[1];

		Camera m_Camera;

		bool m_DigPressed;
		bool m_PlacePressed;

		void Awake()
		{
			digAction.action.Enable();
			placeAction.action.Enable();
			TryGetComponent(out m_Camera);
		}

		void Update()
		{
			m_DigPressed = digAction.action.IsPressed();
			m_PlacePressed = placeAction.action.IsPressed();
		}

		void FixedUpdate()
		{
			ApplyStamp();
		}

		void ApplyStamp()
		{
			var ray = m_Camera.ViewportPointToRay(Vector3.one * .5f);

			if (Physics.RaycastNonAlloc(ray, m_Hits, maxDistance, layerMask) == 0)
				return;

			float3 point = m_Hits[0].point;

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
				stamp.strength = -digStrength / 127f;
				VoxelAPI.Stamp(stamp);
			}
			else if (m_PlacePressed)
			{
				stamp.strength = placeStrength / 127f;
				VoxelAPI.Stamp(stamp);
			}
		}
	}
}
