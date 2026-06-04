using Fusion;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starter.Shooter
{
	[System.Serializable]
	public class NightSFXEntry
	{
		[Tooltip("SFX played on all clients when the delay elapses after nightfall.")]
		public AudioClip Clip;
		[Tooltip("Seconds after nightfall before this SFX plays.")]
		public float Delay = 0f;
		[Range(0f, 1f), Tooltip("Playback volume for this SFX.")]
		public float Volume = 1f;
	}

	/// <summary>
	/// Scene-resident NetworkBehaviour that renders the day/night look (sun, ambient, fog, skybox) and BGM from the
	/// authoritative match phase. <see cref="MatchManager"/> is the single clock: brightness, <see cref="IsNight"/>,
	/// the HUD countdown and the music cues all derive from its phase + phase timer, so the visuals track the round
	/// exactly (full day in Town, a sunset across DuskWarning, deep night during the Purge) instead of a free clock that
	/// would drift past the dusk gap. Late joiners snap to the current phase; there is no transition RPC.
	///
	/// The free-running <see cref="SessionTime"/> cycle survives only as a fallback for isolated test scenes that have
	/// no MatchManager.
	/// </summary>
	public sealed class TimeManager : NetworkBehaviour
	{
		public static TimeManager Instance { get; private set; }

		[Header("Cycle Length fallback (test scenes only)")]
		[Tooltip("Day length used ONLY in isolated scenes with no MatchManager. In a real match, DayLength reads live " +
			"from MatchManager.DayDuration — the single source of truth for phase timing (CLAUDE.md: phase-aware " +
			"systems read the match controller, never a local clock).")]
		public float FallbackDayLength = 90f;
		[Tooltip("Night length used ONLY in isolated scenes with no MatchManager. In a real match, NightLength reads " +
			"live from MatchManager.NightMaxDuration.")]
		public float FallbackNightLength = 30f;

		/// <summary>Daytime phase length in seconds. Reads live from <see cref="MatchManager.DayDuration"/> (the single
		/// source of truth for phase timing); falls back to <see cref="FallbackDayLength"/> only in scenes with no
		/// MatchManager. TimeManager stores no duration of its own — it listens to the match controller.</summary>
		public float DayLength => _match != null ? _match.DayDuration : FallbackDayLength;

		/// <summary>Nighttime phase length in seconds. Reads live from <see cref="MatchManager.NightMaxDuration"/>;
		/// falls back to <see cref="FallbackNightLength"/> only in scenes with no MatchManager.</summary>
		public float NightLength => _match != null ? _match.NightMaxDuration : FallbackNightLength;

		[Header("Sun")]
		[Tooltip("Directional Light rotated by the cycle. If null, falls back to RenderSettings.sun.")]
		public Light Sun;
		[Tooltip("Optional second directional light (e.g. a fill light from another angle). Gets the SAME day↔night " +
			"intensity lerp as the main Sun, but its rotation is never touched.")]
		public Light SecondarySun;
		[Tooltip("Directional light intensity at full daytime brightness.")]
		public float DaySunIntensity = 1f;
		[Tooltip("Directional light intensity at deep night. 0 = no direct sun contribution (ambient still lights the world).")]
		public float NightSunIntensity = 0f;
		[Tooltip("Degrees the sun pitches down (about X) from its authored day rotation as the world goes from full day " +
			"to deep night, so it dips below the horizon at night. ~95° puts it just under.")]
		public float SunSetArcDegrees = 95f;
		[Tooltip("Total degrees the sun sweeps east-to-west (about world up) across the whole Day phase, so it visibly " +
			"tracks time while the town stays bright. Cosmetic horizontal travel only - the sunset/darkening is driven " +
			"separately by the phase brightness. 0 = no daytime travel.")]
		public float DaySunTravelDegrees = 150f;

		[Header("Transition")]
		[Tooltip("Max seconds to ease the visual brightness toward a new target on a hard phase jump (e.g. a debug " +
			"set_nighttime). The DuskWarning sunset ramps over the whole dusk window regardless; this only smooths snaps.")]
		[Min(0.01f)] public float VisualSnapSeconds = 1.5f;

		[Header("Daylight Falloff")]
		[Tooltip("Daylight level (0..1) reached by the END of the Day phase. The light eases from full (1) at dawn " +
			"down to this by the time dusk begins, so the world dims across the whole day. DuskWarning then fades " +
			"from here to 0. 1 = no daytime dimming (full bright until dusk); lower = more visible decline during the day.")]
		[Range(0f, 1f)] public float DuskHandoffBrightness = 0.5f;

		[Header("Ambient & Reflection Intensity")]
		[Tooltip("Ambient light intensity multiplier at full daytime.")]
		public float DayAmbientIntensity = 1f;
		[Tooltip("Ambient light intensity multiplier at deep night.")]
		public float NightAmbientIntensity = 0.2f;
		[Tooltip("Environment reflection intensity at full daytime.")]
		public float DayReflectionIntensity = 1f;
		[Tooltip("Environment reflection intensity at deep night.")]
		public float NightReflectionIntensity = 0.2f;

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

		[Header("Night SFX Sequence")]
		[Tooltip("SFX played server-wide at specific times after nightfall. Each entry has its own clip and delay. Supports up to 32 entries.")]
		public NightSFXEntry[] NightSFXEntries = System.Array.Empty<NightSFXEntry>();

		[Header("BGM")]
		[Tooltip("Music played during the day phase. Leave empty to silence music at dawn.")]
		public AudioClip DayBGM;
		[Tooltip("Music blended in across the DuskWarning transition (Day → Dusk → Night). Leave empty to just fade the " +
			"day music out when dusk hits instead.")]
		public AudioClip DuskBGM;
		[Tooltip("Music played during the night phase. Leave empty to silence music at nightfall.")]
		public AudioClip NightBGM;
		[Tooltip("Fade duration in seconds for each side of the music transition.")]
		public float BGMFadeDuration = 2f;
		[Tooltip("Seconds of silence between the fade-out and fade-in during a day/night music transition.")]
		public float BGMSilenceDuration = 1.5f;

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

		/// <summary>
		/// Bitmask written by state authority: bit i is set when NightSFXEntries[i]'s delay has elapsed.
		/// Reset to 0 at the start of each day. All clients react via OnChangedRender.
		/// </summary>
		[Networked, OnChangedRender(nameof(OnNightSFXMaskChanged))]
		private int NightSFXTriggerMask { get; set; }

		private int _lastNightSFXMask;

		public float FullCycleLength => DayLength + NightLength;

		/// <summary>1-indexed day counter — the match round when driven by <see cref="MatchManager"/>, else the cycle index.</summary>
		public int CurrentDay => _match != null
			? Mathf.Max(1, _match.RoundIndex)
			: Mathf.FloorToInt(SessionTime / FullCycleLength) + 1;

		/// <summary>True once the Purge is on — driven by the match phase (Night/MatchOver) so it agrees with the
		/// visuals, the HUD and sleep gating. Falls back to the free-running cycle only in MatchManager-less test scenes.</summary>
		public bool IsNight => _match != null
			? (_match.Phase == MatchPhase.Night || _match.Phase == MatchPhase.MatchOver)
			: CycleSeconds >= DayLength;

		/// <summary>0..1 progress through the full day+night cycle (fallback test-scene clock only).</summary>
		public float CycleProgress => CycleSeconds / FullCycleLength;

		/// <summary>Seconds remaining in the current phase. Reads the authoritative match phase timer when present so the
		/// HUD countdown matches the round; falls back to the cycle clock in test scenes.</summary>
		public float PhaseRemaining => _match != null
			? _match.RemainingPhaseSeconds
			: (CycleSeconds >= DayLength ? FullCycleLength - CycleSeconds : DayLength - CycleSeconds);

		/// <summary>Seconds elapsed into the current Night phase (used for the timed Night SFX sequence).</summary>
		private float NightElapsedSeconds => _match != null
			? Mathf.Max(0f, _match.NightMaxDuration - _match.RemainingPhaseSeconds)
			: Mathf.Max(0f, CycleSeconds - DayLength);

		/// <summary>0..1 elapsed through a match phase of the given full <paramref name="duration"/>, from its timer.</summary>
		private float PhaseProgress(float duration) =>
			_match != null && duration > 0f ? Mathf.Clamp01(1f - _match.RemainingPhaseSeconds / duration) : 1f;

		/// <summary>Seconds into the current cycle, in [0, FullCycleLength). Fallback test-scene clock only.</summary>
		private float CycleSeconds
		{
			get
			{
				float cycle = FullCycleLength;
				return cycle > 0f ? SessionTime - Mathf.Floor(SessionTime / cycle) * cycle : 0f;
			}
		}

		/// <summary>The match controller this clock listens to for its phase durations, resolved once on Spawned.
		/// Null only in isolated test scenes (then the Fallback* lengths apply).</summary>
		private MatchManager _match;

		/// <summary>AudioListener volume captured when PreNight silences everything, restored when it ends.</summary>
		private float _audioVolumeBeforePreNight = 1f;

		/// <summary>Eased 0..1 daylight level (1 = full day, 0 = deep night), driven by the match phase. Lerped toward
		/// its phase target each frame so debug phase-snaps don't pop and the dusk sunset rolls smoothly.</summary>
		private float _brightness = 1f;

		/// <summary>Tracks the last phase we set music for, so a PhaseChanged replay (late-joiners) doesn't re-trigger.</summary>
		private MatchPhase _lastMusicPhase;
		private bool       _musicInitialized;

		private int  _lastLoggedDay = -1;
		private bool _wasNight;
		private bool _hasLoggedAnything;
		private Quaternion _sunBaseRotation;
		private AmbientMode _originalAmbientMode;
		private bool _ambientModeOverridden;
		private float _originalAmbientIntensity;
		private float _originalReflectionIntensity;

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

			// Resolve the match controller once — DayLength/NightLength read live from it thereafter, so the visual
			// cycle stays locked to the authoritative match durations (CLAUDE.md: phase-aware systems read the match
			// controller, never a local clock). Resolved before any IsNight-dependent setup below (BGM, derived getters).
			// Falls back to the serialized Fallback* lengths in scenes with no MatchManager (isolated test scenes).
			// FindAnyObjectByType covers the case where MatchManager.Spawned hasn't set its Instance yet.
			_match = MatchManager.Instance != null ? MatchManager.Instance : FindAnyObjectByType<MatchManager>();

			// The PreNight beat (final seconds of dusk) silences the whole soundscape; Night restores it. Apply the
			// current state immediately in case MatchManager already fired PreNightChanged before we subscribed.
			MatchManager.PreNightChanged += OnPreNightSilence;
			if (_match != null) OnPreNightSilence(_match.IsPreNight);

			// BGM follows the match phase, not the visual cycle: day music in Town, a hard fade-out the instant dusk
			// hits, night music when the Purge begins.
			MatchManager.PhaseChanged += OnPhaseMusic;

			// Snap the eased brightness to the current phase so a mid-session join doesn't fade in from full day.
			_brightness = TargetBrightness();

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

			// Cache the authored ambient/reflection intensities so we can restore them on despawn.
			_originalAmbientIntensity    = RenderSettings.ambientIntensity;
			_originalReflectionIntensity = RenderSettings.reflectionIntensity;

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
			OnPhaseMusic(_match != null ? _match.Phase : MatchPhase.Day);
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (Instance == this) Instance = null;

			MatchManager.PreNightChanged -= OnPreNightSilence;
			MatchManager.PhaseChanged    -= OnPhaseMusic;
			// Don't let a PreNight mute leak into the menu scene after this manager despawns.
			AudioListener.volume = 1f;

			if (_ambientModeOverridden)
			{
				RenderSettings.ambientMode = _originalAmbientMode;
				_ambientModeOverridden = false;
			}

			RenderSettings.ambientIntensity    = _originalAmbientIntensity;
			RenderSettings.reflectionIntensity = _originalReflectionIntensity;

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

			// In a real match the phase machine is the clock — visuals/IsNight derive from MatchManager directly, so
			// SessionTime is only advanced (and only read) as the fallback for MatchManager-less test scenes.
			if (_match == null) SessionTime += Runner.DeltaTime;

			if (!IsNight)
			{
				// Reset bitmask each day so entries can fire again next night.
				if (NightSFXTriggerMask != 0) NightSFXTriggerMask = 0;
				return;
			}

			// Set a bit for each entry whose delay has elapsed since nightfall.
			float nightElapsed = NightElapsedSeconds;
			int mask = NightSFXTriggerMask;
			for (int i = 0; i < NightSFXEntries.Length && i < 32; i++)
			{
				if (nightElapsed >= NightSFXEntries[i].Delay)
					mask |= (1 << i);
			}
			if (mask != NightSFXTriggerMask) NightSFXTriggerMask = mask;
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

		/// <summary>Debug-only: snap the visual cycle to mid-day or mid-night from any peer. Routes to the state
		/// authority; the resulting <see cref="SessionTime"/> jump replicates so all clients re-derive sun/ambient/fog
		/// and the BGM crossfade fires via <see cref="LogPhaseTransitions"/>. Used by the set_daytime / set_nighttime
		/// console commands. Going to day from night advances to the next cycle so the day index/BGM update cleanly.</summary>
		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_DebugSetNight(NetworkBool night)
		{
			if (HasStateAuthority == false) return;

			float cycle = FullCycleLength;
			if (cycle <= 0f) return;

			float cycleStart = Mathf.Floor(SessionTime / cycle) * cycle;
			if (night)
			{
				SessionTime = cycleStart + DayLength + NightLength * 0.5f; // middle of this cycle's night
			}
			else
			{
				float target = cycleStart + DayLength * 0.5f;             // middle of this cycle's day
				if (IsNight) target += cycle;                            // already night → jump to next morning
				SessionTime = target;
			}
		}

		/// <summary>Debug-only: jump the visual cycle to <paramref name="cycleSeconds"/> into the current day+night cycle
		/// (clamped to [0, FullCycleLength]). Routes to the state authority and writes <see cref="SessionTime"/>, so the
		/// sun rotation, ambient palette, fog and skybox blend re-derive on every peer from the replicated value — no
		/// per-effect RPC. Used by <c>trigger_night</c> to park the sky just before twilight so the sun sets in step with
		/// the dusk siren. The clock keeps advancing from here, so the sunset plays out naturally.</summary>
		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_DebugSetCycleSeconds(float cycleSeconds)
		{
			if (HasStateAuthority == false) return;

			float cycle = FullCycleLength;
			if (cycle <= 0f) return;

			float cycleStart = Mathf.Floor(SessionTime / cycle) * cycle;
			SessionTime = cycleStart + Mathf.Clamp(cycleSeconds, 0f, cycle);
		}

		private void Update()
		{
			if (Object == null || Object.IsValid == false) return;

			// Insurance against spawn-order: if MatchManager hadn't set its Instance (or wasn't findable) when we spawned,
			// _match would be null and every getter would silently fall back to the free-running test clock — making the
			// visuals ignore the match entirely. Pick it up the moment it exists so brightness/sun track the real phase.
			if (_match == null && MatchManager.Instance != null)
				_match = MatchManager.Instance;

			// Ease toward the phase's brightness target. The DuskWarning target ramps continuously over the dusk window
			// (so the sunset tracks it tightly); only hard phase-snaps are speed-limited by VisualSnapSeconds.
			_brightness = Mathf.MoveTowards(_brightness, TargetBrightness(), Time.deltaTime / Mathf.Max(0.01f, VisualSnapSeconds));

			ApplySunRotation();
			ApplyAmbientLerp(_brightness);
			LogPhaseTransitions();
		}

		/// <summary>Phase-driven daylight level (drives directional + ambient/reflection/fog/skybox intensity). Eases down
		/// across the whole Day phase — from full bright at dawn to <see cref="DuskHandoffBrightness"/> by the time the
		/// day ends — so the world visibly dims as time passes instead of holding full until dusk. DuskWarning then
		/// finishes the fade from the handoff level to deep night. Falls back to the free-running cycle curve in
		/// MatchManager-less test scenes.</summary>
		private float TargetBrightness()
		{
			if (_match == null) return CycleBrightness();

			switch (_match.Phase)
			{
				case MatchPhase.Lobby:
					return 1f;
				case MatchPhase.Day:
					// Gentle decline across the day: 1 (dawn) → DuskHandoffBrightness (day's end). The light starts
					// dropping the moment the day starts; dusk is only the final plunge to black.
					return Mathf.Lerp(1f, DuskHandoffBrightness, PhaseProgress(_match.DayDuration));
				case MatchPhase.DuskWarning:
					// Final period: hand off from the day's end level down to deep night across the dusk window.
					return Mathf.Lerp(DuskHandoffBrightness, 0f, PhaseProgress(_match.DuskWarningDuration));
				case MatchPhase.Night:
				case MatchPhase.MatchOver:
					return 0f;
				default:
					return 1f;
			}
		}

		/// <summary>0..1 amount the sun has set, used only for its pitch — kept SEPARATE from the daylight brightness so
		/// the sun stays fully up while the light dims across the day, then drops only during the final dusk window.
		/// 0 all day (sun up), ramps 0→1 across DuskWarning (the sunset), 1 at night (below horizon).</summary>
		private float SunSetAmount01()
		{
			if (_match == null) return Mathf.Clamp01(1f - CycleBrightness());

			switch (_match.Phase)
			{
				case MatchPhase.Lobby:
				case MatchPhase.Day:
					return 0f;
				case MatchPhase.DuskWarning:
					return PhaseProgress(_match.DuskWarningDuration);
				case MatchPhase.Night:
				case MatchPhase.MatchOver:
					return 1f;
				default:
					return 0f;
			}
		}

		/// <summary>Original free-running brightness curve, kept only for MatchManager-less test scenes.</summary>
		private float CycleBrightness()
		{
			float t          = CycleSeconds;
			float dawnEnd    = Mathf.Min(DawnRampSeconds, DayLength);
			float twilightAt = DayLength * TwilightStart;

			if (t < dawnEnd)    return dawnEnd > 0f ? t / dawnEnd : 1f;
			if (t < twilightAt) return 1f;
			if (t < DayLength)
			{
				float fadeSpan = DayLength - twilightAt;
				return fadeSpan > 0f ? 1f - (t - twilightAt) / fadeSpan : 0f;
			}
			return 0f;
		}

		/// <summary>0..1 progress of the sun's east→west daytime traversal, taken from the match Day-phase timer so it
		/// tracks the round, not a free clock. Advances across the Day phase and holds at 1 through Dusk/Night (the sun
		/// has finished crossing the sky and is now setting). This drives only the cosmetic horizontal sweep; the actual
		/// sunset/darkening rides the phase brightness. Falls back to the free cycle's day portion in MatchManager-less
		/// test scenes.</summary>
		private float DayArcProgress01()
		{
			if (_match == null) return Mathf.Clamp01(CycleSeconds / Mathf.Max(0.0001f, DayLength));

			switch (_match.Phase)
			{
				case MatchPhase.Lobby:
					return 0f;                                   // pre-match: sun at the eastern (morning) end
				case MatchPhase.Day:
					return PhaseProgress(_match.DayDuration);    // east → west across the day
				default:
					return 1f;                                   // Dusk/Night/MatchOver: traversal complete (western end)
			}
		}

		private void ApplySunRotation()
		{
			if (Sun == null) return;

			// Two independent, phase-driven motions composed onto the authored sun rotation:
			//  • Pitch (about the authored east axis) tracks SunSetAmount01 — 0° all day so the sun stays up while the
			//    light dims, then swinging down to SunSetArcDegrees below the horizon across the DuskWarning window. This
			//    is kept SEPARATE from the daylight brightness on purpose: the world darkens through the day, but the sun
			//    only actually sets during the final dusk period.
			//  • Azimuth (about world up) sweeps the sun east→west across the Day phase so it visibly tracks time while
			//    the town stays bright, then holds at the western end through Dusk/Night so the sun sets in the west.
			float setAngle = SunSetAmount01() * SunSetArcDegrees;
			float azimuth  = (DayArcProgress01() - 0.5f) * DaySunTravelDegrees;
			Sun.transform.rotation =
				Quaternion.AngleAxis(azimuth, Vector3.up) * Quaternion.AngleAxis(setAngle, Vector3.right) * _sunBaseRotation;
		}

		private void ApplyAmbientLerp(float brightness)
		{
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

			// Drive directional intensity straight from the phase brightness (1 in full day, ramping to 0 across the
			// DuskWarning window, 0 at night), NOT from the sun's elevation. Brightness tracks the match phase timer, so
			// the directional light dims exactly in step with the sunset/Purge regardless of where the sun's pitch lands
			// — the bug just fixed was elevation-based intensity staying high because a short dusk barely moved the sun.
			// SecondarySun (a fill light that never rotates) gets the same lerp so it dims in step.
			float sunIntensity = Mathf.Lerp(NightSunIntensity, DaySunIntensity, brightness);
			if (Sun != null)          Sun.intensity          = sunIntensity;
			if (SecondarySun != null) SecondarySun.intensity = sunIntensity;

			// Fade ambient + reflection intensity on the same brightness curve so the world dims at night instead of
			// staying lit by full-strength skybox ambient/reflections.
			RenderSettings.ambientIntensity    = Mathf.Lerp(NightAmbientIntensity,    DayAmbientIntensity,    brightness);
			RenderSettings.reflectionIntensity = Mathf.Lerp(NightReflectionIntensity, DayReflectionIntensity, brightness);
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

			// BGM is driven from the match phase in OnPhaseMusic now — this is logging only.
			if (night && _wasNight == false)
				Debug.Log($"[TIME] === NIGHTFALL Day {day} ===");
			else if (night == false && day != _lastLoggedDay)
				Debug.Log($"[TIME] === Day {day} START ===");

			_lastLoggedDay = day;
			_wasNight = night;
		}

		private void OnNightSFXMaskChanged()
		{
			int newMask  = NightSFXTriggerMask;
			int newlySet = newMask & ~_lastNightSFXMask;
			_lastNightSFXMask = newMask;

			for (int i = 0; i < NightSFXEntries.Length && i < 32; i++)
				if ((newlySet & (1 << i)) != 0 && NightSFXEntries[i].Clip != null)
					AudioManager.Instance?.PlaySFX2D(NightSFXEntries[i].Clip, NightSFXEntries[i].Volume);
		}

		/// <summary>Cuts the entire soundscape to silence for the PreNight beat (the last seconds of dusk before the
		/// Purge) by muting the global <see cref="AudioListener"/>, then restores it the moment Night begins. Driven by
		/// the networked <see cref="MatchManager.PreNightChanged"/> so every peer falls silent and recovers in lockstep.</summary>
		private void OnPreNightSilence(bool active)
		{
			if (active)
			{
				_audioVolumeBeforePreNight = AudioListener.volume;
				AudioListener.volume = 0f;
			}
			else
			{
				AudioListener.volume = _audioVolumeBeforePreNight;
			}
		}

		/// <summary>Drives BGM straight off the match phase so the timing is exact and tracks Day → Dusk → night by
		/// blending: day music in Town, a crossfade to <see cref="DuskBGM"/> the instant <see cref="MatchPhase.DuskWarning"/>
		/// hits, then a blend out into night music when the Purge begins. With no DuskBGM assigned the dusk step just
		/// fades the day music out instead. Skips a repeated phase (PhaseChanged replays for late-joiners); the first call
		/// after Spawned starts without a crossfade, every later transition crossfades.</summary>
		private void OnPhaseMusic(MatchPhase phase)
		{
			if (_musicInitialized && phase == _lastMusicPhase) return;
			bool crossfade = _musicInitialized;
			_lastMusicPhase   = phase;
			_musicInitialized = true;

			switch (phase)
			{
				case MatchPhase.Lobby:
				case MatchPhase.Day:
					PlayBGM(DayBGM, crossfade);
					break;
				case MatchPhase.DuskWarning:
					PlayBGM(DuskBGM, crossfade); // blend Day → Dusk (or fade out if no dusk clip)
					break;
				case MatchPhase.Night:
					PlayBGM(NightBGM, crossfade); // blend Dusk out → night
					break;
				case MatchPhase.MatchOver:
					break; // leave whatever's playing through the scoreboard
			}
		}

		private void PlayBGM(AudioClip clip, bool crossfade)
		{
			if (clip == null) { AudioManager.Instance?.StopMusic(BGMFadeDuration); return; }
			if (crossfade)
				AudioManager.Instance?.CrossfadeMusic(clip, loop: true, fadeDuration: BGMFadeDuration);
			else
				AudioManager.Instance?.PlayMusic(clip, loop: true, fadeDuration: BGMFadeDuration);
		}
	}
}
