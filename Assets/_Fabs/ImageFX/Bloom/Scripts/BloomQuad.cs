namespace PostEffects
{
	using UnityEngine;

	[ExecuteInEditMode]
	[RequireComponent(typeof(MeshFilter))]
	[RequireComponent(typeof(MeshRenderer))]
	public class BloomQuad : MonoBehaviour
	{
		[SerializeField]
		Camera mainCamera;

		[SerializeField]
		LayerMask layerMask;

		[Header("Core")]
		[SerializeField]
		Shader bloomShader;

		[SerializeField]
		Shader displayShader;

		[SerializeField]
		Texture2D noise;

		[Header("Settings")]
		[Range(256, 1024)]
		public int resolution = 512;

		[Range(2, 8)]
		public int iterations = 8;

		[Range(0, 10)]
		public float intensity = 0.8f;

		[Range(0, 10)]
		public float threshold = 0.6f;

		[Range(0, 1)]
		public float softKnee = 0.7f;

		readonly int _BloomTex = Shader.PropertyToID("_BloomTex");
		readonly int _NoiseTex = Shader.PropertyToID("_NoiseTex");
		readonly int _NoiseTexScale = Shader.PropertyToID("_NoiseTexScale");
		Bloom bloom;
		RenderTexture bloomTarget;

		Material displayMaterial;

		bool inited;

		void Awake()
		{
			Init();
		}

		void Update()
		{
			Init();
			if (!inited)
				return;
			if (mainCamera == null)
				return;

			if (bloom == null)
				bloom = new Bloom(bloomShader);

			bloom.iterations = iterations;
			bloom.intensity = intensity;
			bloom.threshold = threshold;
			bloom.softKnee = softKnee;

			var res = RenderTextureUtils.GetScreenResolution(resolution);
			var sourceTarget = GetTarget(res, Ext.argbHalf);
			bloomTarget = GetTarget(res, Ext.argbHalf);

			mainCamera.targetTexture = sourceTarget;
			var mask = mainCamera.cullingMask;
			mainCamera.cullingMask ^= layerMask.value;
			mainCamera.Render();
			mainCamera.cullingMask = mask;
			mainCamera.targetTexture = null;

			bloom.Apply(sourceTarget, bloomTarget, res);

			displayMaterial.SetTexture(_BloomTex, bloomTarget);
			displayMaterial.SetTexture(_NoiseTex, noise);
			displayMaterial.SetVector(_NoiseTexScale, RenderTextureUtils.GetTextureScreenScale(noise));

			RenderTexture.ReleaseTemporary(sourceTarget);
		}

		void LateUpdate()
		{
			if (bloomTarget != null)
				RenderTexture.ReleaseTemporary(bloomTarget);
			bloomTarget = null;
		}

		void OnDestroy()
		{
			if (bloom != null)
				bloom.Dispose();
			DestroyFunc(displayMaterial);
			var meshFilter = GetComponent<MeshFilter>();
			DestroyFunc(meshFilter.sharedMesh);
		}

		void Init()
		{
			if (inited)
				return;
			if (displayShader == null)
				return;
			if (bloomShader == null)
				return;
			inited = true;

			displayMaterial = new Material(displayShader);

			var meshRenderer = GetComponent<MeshRenderer>();
			meshRenderer.sharedMaterial = displayMaterial;

			var meshFilter = GetComponent<MeshFilter>();
			meshFilter.sharedMesh = CreateQuadMesh();
		}

		void DestroyFunc(Object obj)
		{
			if (obj == null)
				return;
			if (Application.isPlaying)
				Destroy(obj);
			else
				DestroyImmediate(obj);
		}

		RenderTexture GetTarget(Vector2Int res, RenderTextureFormat format)
		{
			var target = RenderTexture.GetTemporary(res.x, res.y, 0, format);
			target.filterMode = FilterMode.Bilinear;
			target.wrapMode = TextureWrapMode.Clamp;
			return target;
		}

		Mesh CreateQuadMesh()
		{
			var mesh = new Mesh();
			mesh.hideFlags = HideFlags.HideAndDontSave;
			mesh.vertices = new Vector3[4] { new(-1, -1, 0), new(-1, 1, 0), new(1, 1, 0), new(1, -1, 0) };
			mesh.uv = new Vector2[4] { new(0, 0), new(0, 1), new(1, 1), new(1, 0) };
			mesh.triangles = new int[6] { 2, 1, 0, 0, 3, 2 };
			return mesh;
		}

		Material CreateMaterial(Shader shader)
		{
			var material = new Material(shader);
			material.hideFlags = HideFlags.HideAndDontSave;
			return material;
		}
	}
}
