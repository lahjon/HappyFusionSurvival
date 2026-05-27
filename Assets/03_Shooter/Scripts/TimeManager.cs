using Fusion;
using UnityEngine;

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

		public override void Spawned()
		{
			Instance = this;

			// Cache the sun's authored rotation so the X-axis spin preserves the scene's Y/Z arc.
			if (Sun == null) Sun = RenderSettings.sun;
			if (Sun != null) _sunBaseRotation = Sun.transform.rotation;
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (Instance == this) Instance = null;
		}

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false) return;
			SessionTime += Runner.DeltaTime;
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
			}
			else if (night == false && day != _lastLoggedDay)
			{
				Debug.Log($"[TIME] === Day {day} START ===");
			}

			_lastLoggedDay = day;
			_wasNight = night;
		}
	}
}
