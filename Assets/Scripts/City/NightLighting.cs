using UnityEngine;

namespace Starter.City
{
	/// <summary>
	/// Owns the night. HorrorTown is permanently the middle of the night — moonlight and street lamps are the
	/// only light sources — so this component sets the whole environment look in one place: near-black blue
	/// ambient, distance fog, and every directional light in the scene turned into one pale moon.
	///
	/// <para>Runs in the editor too (<c>ExecuteAlways</c>) so the scene view shows the same night the players
	/// get, and re-applies on enable at runtime so nothing (like the legacy day/night <c>TimeManager</c>, which
	/// it disables) can drag the scene back to daylight.</para>
	/// </summary>
	[ExecuteAlways]
	public sealed class NightLighting : MonoBehaviour
	{
		[Header("Moon")]
		[Tooltip("Moonlight intensity. Enough to silhouette rooftops, not enough to read an alley by.")]
		[Min(0f)] public float MoonIntensity = 0.28f;
		public Color MoonColor = new Color(0.58f, 0.66f, 0.90f);
		[Tooltip("Moon direction (pitch, yaw).")]
		public Vector2 MoonAngles = new Vector2(52f, -38f);

		[Header("Ambient / fog")]
		public Color AmbientColor = new Color(0.030f, 0.040f, 0.075f);
		public Color FogColor = new Color(0.008f, 0.012f, 0.026f);
		[Min(0f)] public float FogDensity = 0.0042f;

		private void OnEnable() => Apply();

		private void Start() => Apply();

		/// <summary>Idempotent — safe to call any time the environment needs re-asserting.</summary>
		public void Apply()
		{
			RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
			RenderSettings.ambientLight = AmbientColor;
			RenderSettings.skybox = null; // black night sky; the skyline reads from lit windows and lamps
			RenderSettings.fog = true;
			RenderSettings.fogMode = FogMode.ExponentialSquared;
			RenderSettings.fogColor = FogColor;
			RenderSettings.fogDensity = FogDensity;
			RenderSettings.reflectionIntensity = 0.15f;

			ConfigureLights();
			DisableDayCycle();
		}

		/// <summary>First directional light becomes the moon; any others are switched off so a second authored
		/// sun cannot wash the night out.</summary>
		private void ConfigureLights()
		{
			bool moonAssigned = false;
			var lights = FindObjectsByType<Light>();

			for (int i = 0; i < lights.Length; i++)
			{
				if (lights[i].type != LightType.Directional) continue;

				if (!moonAssigned)
				{
					moonAssigned = true;
					var moon = lights[i];
					moon.color = MoonColor;
					moon.intensity = MoonIntensity;
					moon.shadows = LightShadows.Soft;
					moon.transform.rotation = Quaternion.Euler(MoonAngles.x, MoonAngles.y, 0f);
					RenderSettings.sun = moon;
				}
				else
				{
					lights[i].intensity = 0f;
					lights[i].enabled = false;
				}
			}
		}

		/// <summary>The Purge-era day/night driver would fight this component every frame — night is not a phase
		/// here, it is the world.</summary>
		private void DisableDayCycle()
		{
			var timeManager = FindAnyObjectByType<Starter.Shooter.TimeManager>();
			if (timeManager != null && timeManager.enabled) timeManager.enabled = false;
		}
	}
}
