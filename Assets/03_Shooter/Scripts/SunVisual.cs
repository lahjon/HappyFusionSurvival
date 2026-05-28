using UnityEngine;
using UnityEngine.Rendering;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only visual sun. Spawns a single emissive sphere and keeps it anchored to the
	/// main camera in the direction opposite the directional sun's forward axis, so the disc
	/// tracks the day/night cycle without parallax. Color is pushed into HDR range so the URP
	/// bloom pass picks it up.
	/// </summary>
	[ExecuteAlways]
	public sealed class SunVisual : MonoBehaviour
	{
		[Tooltip("Directional light the sun follows. If null, falls back to TimeManager.Instance.Sun then RenderSettings.sun.")]
		public Light SunLight;

		[Tooltip("Distance from the camera at which the disc is rendered. Must be inside the camera's far clip.")]
		public float SkyDistance = 800f;

		[Tooltip("World-space diameter of the disc at SkyDistance.")]
		public float SunSize = 80f;

		[Tooltip("Color of the disc before HDR scaling.")]
		[ColorUsage(false, true)]
		public Color SunColor = new Color(1f, 0.95f, 0.8f);

		[Tooltip("HDR multiplier applied to SunColor. Values above bloom threshold (~1.0) trigger the glow.")]
		public float Intensity = 15f;

		[Tooltip("Hide the disc when the sun is below the horizon (light pointing upwards).")]
		public bool HideAtNight = true;

		private Transform    _disc;
		private MeshRenderer _renderer;
		private Material     _material;
		private Camera       _camera;

		private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

		private void OnEnable()
		{
			EnsureDisc();
		}

		private void EnsureDisc()
		{
			if (_disc != null && _renderer != null && _material != null) return;

			var existing = transform.Find("SunDisc");
			GameObject discGO;
			if (existing != null)
			{
				discGO = existing.gameObject;
			}
			else
			{
				discGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
				discGO.name = "SunDisc";
				discGO.transform.SetParent(transform, false);
				var col = discGO.GetComponent<Collider>();
				if (col != null)
				{
					if (Application.isPlaying) Destroy(col);
					else                       DestroyImmediate(col);
				}
			}

			_disc     = discGO.transform;
			_renderer = discGO.GetComponent<MeshRenderer>();
			_renderer.shadowCastingMode          = ShadowCastingMode.Off;
			_renderer.receiveShadows             = false;
			_renderer.allowOcclusionWhenDynamic  = false;
			_renderer.lightProbeUsage            = LightProbeUsage.Off;
			_renderer.reflectionProbeUsage       = ReflectionProbeUsage.Off;
			_renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

			// URP/Unlit writes _BaseColor straight to the framebuffer. Avoids URP/Lit's _EMISSION
			// shader_feature, which gets variant-stripped from Play-mode builds and silently
			// fails to enable at runtime. _BaseColor is multiplied unclamped, so HDR values pass.
			var shader = Shader.Find("Universal Render Pipeline/Unlit");
			_material = new Material(shader) { name = "SunVisualMat" };
			_material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
			_renderer.sharedMaterial = _material;
		}

		private void LateUpdate()
		{
			EnsureDisc();
			if (_disc == null) return;

			var light = ResolveLight();
			var cam   = ResolveCamera();
			if (light == null || cam == null)
			{
				_renderer.enabled = false;
				return;
			}

			Vector3 sunDir = -light.transform.forward;
			bool aboveHorizon = sunDir.y > -0.05f;

			_renderer.enabled = aboveHorizon || HideAtNight == false;
			if (_renderer.enabled == false) return;

			_disc.position   = cam.transform.position + sunDir * SkyDistance;
			_disc.localScale = Vector3.one * SunSize;

			_material.SetColor(BaseColorId, SunColor * Mathf.Max(Intensity, 0f));
		}

		private Camera ResolveCamera()
		{
			if (_camera != null && _camera.isActiveAndEnabled) return _camera;
			_camera = Camera.main;
			return _camera;
		}

		private Light ResolveLight()
		{
			if (SunLight != null) return SunLight;
			var tm = TimeManager.Instance;
			if (tm != null && tm.Sun != null) return tm.Sun;
			return RenderSettings.sun;
		}
	}
}
