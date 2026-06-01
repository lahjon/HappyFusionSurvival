using Fusion;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starter.Shooter
{
	/// <summary>
	/// Scene-resident NetworkBehaviour that owns the day/night cycle clock.
	/// State authority advances a single <see cref="SessionTime"/> float each tick; everything else
	/// (day index, phase, remaining seconds, sun rotation, ambient colors) is derived locally so
	/// there is no transition RPC, no drift, and late joiners snap to the authority's value automatically.
	/// </summary>
	public sealed class TimeManager : NetworkBehaviour
	{
		public static TimeManager Instance { get; private set; }

		[Header("Cycle Length (seconds)")]
		[Tooltip("Length of the daytime phase in real seconds. Default 90s for fast iteration; spec target is 900s (15 min).")]
		public float DayLength = 90f;
		[Tooltip("Length of the nighttime phase in real seconds. Default 30s for fast iteration; spec target is 300s (5 min).")]
		public float NightLength = 30f;

		[Header("Sun")]
		[Tooltip("Directional Light rotated by the cycle. If null, falls back to RenderSettings.sun.")]
		public Light Sun;
		[Tooltip("Directional light intensity at full daytime brightness.")]
		public float DaySunIntensity = 1f;
		[Tooltip("Directional light intensity at deep night. 0 = no direct sun contribution (ambient still lights the world).")]
		public float NightSunIntensity = 0f;

		[Header("Skybox")]
		[Tooltip("Skybox material used during daytime. If null, uses whatever is set in Lighting → Environment at startup.")]
		public Material DaySkyboxMaterial;
		[Tooltip("Skybox material used during nighttime. If null, skybox is not changed.")]
		public Material NightSkyboxMaterial;
		[Tooltip("Uncheck to leave the skybox alone (e.g. if you're driving it from a separate system).")]
		public bool DriveSkybox = true;

		[Header("Fog")]
		[Tooltip("Uncheck to leave fog settings alone.")]
		public bool DriveFog = true;
		public Color DayFogColor   = new Color(0.55f, 0.65f, 0.75f);
		[Tooltip("Exponential fog density during the day. Keep very low for subtle atmosphere.")]
		public float DayFogDensity = 0.001f;
		public Color NightFogColor   = new Color(0.02f, 0.02f, 0.06f);
		[Tooltip("Exponential fog density at deep night.")]
		public float NightFogDensity = 0.02f;

		[Header("BGM")]
		[Tooltip("Music played during the day phase. Leave empty to silence music at dawn.")]
		public AudioClip DayBGM;
		[Tooltip("Music played during the night phase. Leave empty to silence music at nightfall.")]
		public AudioClip NightBGM;
		[Tooltip("Crossfade duration in seconds when switching between day and night BGM.")]
		public float BGMFadeDuration = 2f;

		[Header("Ambient Palette")]
		[Tooltip("Sky / equator / ground colors at full daytime. Defaults match the scene's existing spherical ambient.")]
		public Color DaySkyColor      = new Color(0.90f, 0.93f, 1.00f);
		public Color DayEquatorColor  = new Color(0.114f, 0.125f, 0.133f);
		public Color DayGroundColor   = new Color(0.047f, 0.043f, 0.035f);
		[Tooltip("Sky / equator / ground colors at deep night. Default = ~10% of day palette with a slight blue shift.")]
		public Color NightSkyColor     = new Color(0.05f, 0.06f, 0.12f);
		public Color NightEquatorColor = new Color(0.02f, 0.025f, 0.04f);
		public Color NightGroundColor  = new Color(0.01f, 0.01f, 0.015f);

		[Tooltip("Fraction of the daytime phase spent at full brightness before the twilight fade begins. 0.8 = bright for the first 80% of day, fade across the last 20%.")]
		[Range(0f, 1f)]
		public float TwilightStart = 0.8f;
		[Tooltip("Seconds at the start of each day spent ramping back up from night to day brightness.")]
		public float DawnRampSeconds = 5f;

		/// <summary>Seconds elapsed since the manager spawned. Authority-only write; everything else derives from this.</summary>
		[Networked]
		public float SessionTime { get; private set; }

		public float FullCycleLength => DayLength + NightLength;

		/// <summary>1-indexed day counter. Day 1 = the first cycle.</summary>
		public int CurrentDay => Mathf.FloorToInt(SessionTime / FullCycleLength) + 1;

		/// <summary>True during the nighttime phase of the current cycle.</summary>
		public bool IsNight => CycleSeconds >= DayLength;

		/// <summary>0..1 progress through the full day+night cycle.</summary>
		public float CycleProgress => CycleSeconds / FullCycleLength;

		/// <summary>Seconds remaining in the current phase (counts down to nightfall during day, to dawn during night).</summary>
		public float PhaseRemaining => IsNight
			? FullCycleLength - CycleSeconds
			: DayLength       - CycleSeconds;

		/// <summary>Seconds into the current cycle, in [0, FullCycleLength). Cached helper.</summary>
		private float CycleSeconds
		{
			get
			{
				float cycle = FullCycleLength;
				return cycle > 0f ? SessionTime - Mathf.Floor(SessionTime / cycle) * cycle : 0f;
			}
		}

		private int  _lastLoggedDay = -1;
		private bool _wasNight;
		private bool _hasLoggedAnything;
		private Quaternion _sunBaseRotation;
		private AmbientMode _originalAmbientMode;
		private bool _ambientModeOverridden;

		// Skybox
		private Material _daySkyboxMaterial;
		private Material _runtimeSkybox;
		private bool _useBlendedSkybox;
		private float _lastSkyboxBlend = -1f;
		private float _dynamicGITimer;

		// Fog
		private bool  _fogWasEnabled;
		private Color _originalFogColor;
		private float _originalFogDensity;
		private FogMode _originalFogMode;

		public override void Spawned()
		{
			Instance = this;

			// Cache the sun's authored rotation so the X-axis spin preserves the scene's Y/Z arc.
			if (Sun == null) Sun = RenderSettings.sun;
			if (Sun != null) _sunBaseRotation = Sun.transform.rotation;

			// Trilight is the only ambientMode where the sky/equator/ground fields apply at runtime;
			// Skybox/Flat silently ignore them, which would make the day-night lerp invisible.
			_originalAmbientMode = RenderSettings.ambientMode;
			if (_originalAmbientMode != AmbientMode.Trilight)
			{
				RenderSettings.ambientMode = AmbientMode.Trilight;
				_ambientModeOverridden = true;
			}

			// If a day skybox is explicitly assigned, apply it now; otherwise fall back to whatever
			// the scene already has set in Lighting → Environment.
			if (DriveSkybox && DaySkyboxMaterial != null)
				RenderSettings.skybox = DaySkyboxMaterial;
			_daySkyboxMaterial = RenderSettings.skybox;

			// Build a runtime skybox that can blend between day and night without dirtying assets.
			if (DriveSkybox && _daySkyboxMaterial != null && NightSkyboxMaterial != null)
			{
				// Skybox/Blended takes two cubemaps + a _Blend float — perfect for cubemap skyboxes.
				// For procedural/other shaders fall back to Material.Lerp on a cloned material.
				var blendShader = Shader.Find("Skybox/CubemapBlend");
				if (blendShader != null
					&& _daySkyboxMaterial.shader.name == "Skybox/Cubemap"
					&& NightSkyboxMaterial.shader.name  == "Skybox/Cubemap")
				{
					_runtimeSkybox = new Material(blendShader) { name = "Skybox (blended runtime)" };
					_runtimeSkybox.SetTexture("_Tex",      _daySkyboxMaterial.GetTexture("_Tex"));
					_runtimeSkybox.SetTexture("_Tex2",     NightSkyboxMaterial.GetTexture("_Tex"));
					_runtimeSkybox.SetColor("_Tint",       _daySkyboxMaterial.GetColor("_Tint"));
					_runtimeSkybox.SetFloat("_Exposure",   _daySkyboxMaterial.GetFloat("_Exposure"));
					_runtimeSkybox.SetFloat("_Rotation",   _daySkyboxMaterial.GetFloat("_Rotation"));
					_runtimeSkybox.SetFloat("_Blend", 0f);
					_useBlendedSkybox = true;
				}
				else
				{
					_runtimeSkybox = new Material(_daySkyboxMaterial) { name = _daySkyboxMaterial.name + " (runtime)" };
					_useBlendedSkybox = false;
				}
				RenderSettings.skybox = _runtimeSkybox;
			}

			// Cache fog state and enable exponential fog under our control.
			_fogWasEnabled    = RenderSettings.fog;
			_originalFogColor   = RenderSettings.fogColor;
			_originalFogDensity = RenderSettings.fogDensity;
			_originalFogMode    = RenderSettings.fogMode;
			if (DriveFog)
			{
				RenderSettings.fog     = true;
				RenderSettings.fogMode = FogMode.Exponential;
			}

			// Play the correct BGM immediately — handles late joiners snapping to mid-session phase.
			PlayBGMForPhase(IsNight, crossfade: false);
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (Instance == this) Instance = null;

			if (_ambientModeOverridden)
			{
				RenderSettings.ambientMode = _originalAmbientMode;
				_ambientModeOverridden = false;
			}

			if (DriveSkybox)
			{
				RenderSettings.skybox = _daySkyboxMaterial;
				if (_runtimeSkybox != null)
				{
					UnityEngine.Object.Destroy(_runtimeSkybox);
					_runtimeSkybox = null;
				}
				DynamicGI.UpdateEnvironment();
			}

			if (DriveFog)
			{
				RenderSettings.fog        = _fogWasEnabled;
				RenderSettings.fogColor   = _originalFogColor;
				RenderSettings.fogDensity = _originalFogDensity;
				RenderSettings.fogMode    = _originalFogMode;
			}
		}

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false) return;
			SessionTime += Runner.DeltaTime;
		}

		/// <summary>State-authority-only: snap <see cref="SessionTime"/> forward to the start of the next day cycle.
		/// Called by <see cref="GameManager"/> when every player is asleep, so morning arrives instantly.
		/// All clients re-derive day/phase/sun from the replicated <see cref="SessionTime"/> jump.</summary>
		public void AdvanceToNextMorning()
		{
			if (HasStateAuthority == false) return;

			float cycle = FullCycleLength;
			if (cycle <= 0f) return;

			// Add a tiny epsilon so we always advance to the NEXT cycle boundary even if SessionTime
			// happens to already sit exactly on a cycle start (e.g. day 1 second 0).
			SessionTime = Mathf.Ceil((SessionTime + 0.001f) / cycle) * cycle;
		}

		private void Update()
		{
			if (Object == null || Object.IsValid == false) return;

			ApplySunRotation();
			ApplyAmbientLerp();
			LogPhaseTransitions();
		}

		private void ApplySunRotation()
		{
			if (Sun == null) return;
			// One full revolution per cycle. Rotate on the world X axis so the sun arcs up and over;
			// pre-multiplying preserves the scene's authored Y/Z orientation (the east-west tilt).
			float xSpin = CycleProgress * 360f;
			Sun.transform.rotation = Quaternion.AngleAxis(xSpin, Vector3.right) * _sunBaseRotation;
		}

		private void ApplyAmbientLerp()
		{
			// Brightness curve over the full cycle:
			//   [0, dawnRamp)               : ramp 0 -> 1 (sharp dawn)
			//   [dawnRamp, day*TwilightStart): hold 1
			//   [day*TwilightStart, day)    : fade 1 -> 0 (twilight)
			//   [day, fullCycle)            : hold 0 (night)
			float t = CycleSeconds;
			float dawnEnd     = Mathf.Min(DawnRampSeconds, DayLength);
			float twilightAt  = DayLength * TwilightStart;

			float brightness;
			if (t < dawnEnd)
			{
				brightness = dawnEnd > 0f ? t / dawnEnd : 1f;
			}
			else if (t < twilightAt)
			{
				brightness = 1f;
			}
			else if (t < DayLength)
			{
				float fadeSpan = DayLength - twilightAt;
				brightness = fadeSpan > 0f ? 1f - (t - twilightAt) / fadeSpan : 0f;
			}
			else
			{
				brightness = 0f;
			}

			RenderSettings.ambientSkyColor     = Color.Lerp(NightSkyColor,     DaySkyColor,     brightness);
			RenderSettings.ambientEquatorColor = Color.Lerp(NightEquatorColor, DayEquatorColor, brightness);
			RenderSettings.ambientGroundColor  = Color.Lerp(NightGroundColor,  DayGroundColor,  brightness);

			// Lerp skybox properties between day and night on a runtime material clone.
			// DynamicGI.UpdateEnvironment is throttled — calling it every frame is too expensive.
			if (DriveSkybox && _runtimeSkybox != null && NightSkyboxMaterial != null)
			{
				float blend = 1f - brightness;
				if (Mathf.Abs(blend - _lastSkyboxBlend) > 0.001f)
				{
					if (_useBlendedSkybox)
						_runtimeSkybox.SetFloat("_Blend", blend);
					else
						_runtimeSkybox.Lerp(_daySkyboxMaterial, NightSkyboxMaterial, blend);
					_lastSkyboxBlend = blend;
					_dynamicGITimer -= Time.deltaTime;
					if (_dynamicGITimer <= 0f)
					{
						DynamicGI.UpdateEnvironment();
						_dynamicGITimer = 1.5f;
					}
				}
			}

			// Lerp fog color and density alongside the brightness curve.
			if (DriveFog)
			{
				RenderSettings.fogColor   = Color.Lerp(NightFogColor,   DayFogColor,   brightness);
				RenderSettings.fogDensity = Mathf.Lerp(NightFogDensity, DayFogDensity, brightness);
			}

			if (Sun != null)
			{
				// Drive intensity from the sun's actual elevation rather than the piecewise brightness
				// curve, so the arc is smooth across the whole day (not flat for 70+ seconds and then
				// only fading during twilight). -forward.y is positive when the sun shines downward.
				float elevation = Mathf.Clamp01(-Sun.transform.forward.y);
				Sun.intensity = Mathf.Lerp(NightSunIntensity, DaySunIntensity, elevation);
			}
		}

		private void LogPhaseTransitions()
		{
			int  day   = CurrentDay;
			bool night = IsNight;

			if (_hasLoggedAnything == false)
			{
				_lastLoggedDay = day;
				_wasNight = night;
				_hasLoggedAnything = true;
				Debug.Log($"[TIME] === Day {day} START === (cycle {DayLength:F0}s day + {NightLength:F0}s night)");
				return;
			}

			if (night && _wasNight == false)
			{
				Debug.Log($"[TIME] === NIGHTFALL Day {day} ===");
				PlayBGMForPhase(isNight: true, crossfade: true);
			}
			else if (night == false && day != _lastLoggedDay)
			{
				Debug.Log($"[TIME] === Day {day} START ===");
				PlayBGMForPhase(isNight: false, crossfade: true);
			}

			_lastLoggedDay = day;
			_wasNight = night;
		}

		private void PlayBGMForPhase(bool isNight, bool crossfade)
		{
			var clip = isNight ? NightBGM : DayBGM;
			if (clip == null) { AudioManager.Instance?.StopMusic(); return; }
			if (crossfade)
				AudioManager.Instance?.CrossfadeMusic(clip, loop: true, fadeDuration: BGMFadeDuration);
			else
				AudioManager.Instance?.PlayMusic(clip, loop: true, fadeDuration: BGMFadeDuration);
		}
	}
}
