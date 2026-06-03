using System;
using System.Collections;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Town-wide audible "the Purge is coming" warning. This is a local-only view of the
	/// networked match phase: every peer receives <see cref="MatchManager.PhaseChanged"/>, so the
	/// siren plays identically on all clients without any networked state of its own (per the
	/// project rule that audio/UI cues are local reads of the single networked phase).
	///
	/// The first warning fires when the round enters <see cref="MatchPhase.DuskWarning"/> — the
	/// ~30s window the phase machine already inserts between Day and Night (see
	/// <see cref="MatchManager.DuskWarningDuration"/>). The siren loops through DuskWarning and
	/// stops once Night begins (or on any other phase). NPC retreat keys off the same transition.
	///
	/// Scene-placed at a town landmark (a tower/speaker). Plays through its own 3D AudioSource so
	/// it's positional; a HUD banner can subscribe to <see cref="WarningRaised"/> without coupling.
	/// </summary>
	[RequireComponent(typeof(AudioSource))]
	public sealed class WorldSiren : MonoBehaviour
	{
		[Header("Audio")]
		[Tooltip("Looping siren clip, played on this component's own 3D AudioSource.")]
		public AudioClip SirenClip;
		[Range(0f, 1f)] public float Volume = 1f;
		[Tooltip("Fade-in seconds when the siren starts.")]
		[Min(0f)] public float FadeInSeconds = 0.75f;
		[Tooltip("Fade-out seconds when the siren stops.")]
		[Min(0f)] public float FadeOutSeconds = 1.5f;

		[Header("Warning banner")]
		[Tooltip("Message broadcast on WarningRaised when the siren first sounds. A HUD banner listens for this.")]
		[TextArea] public string WarningMessage = "The Purge begins soon — get to shelter.";

		/// <summary>Raised on every peer when the dusk warning first sounds. Args: (message, seconds until Night).
		/// A HUD banner subscribes; kept separate from this component so the siren has no UI dependency.</summary>
		public static event Action<string, float> WarningRaised;

		/// <summary>Raised on every peer when the warning window ends (Night begins, or the phase otherwise
		/// leaves DuskWarning — e.g. a debug jump back to Day).</summary>
		public static event Action WarningCleared;

		private AudioSource _source;
		private MatchPhase _appliedPhase;
		private bool _hasApplied;
		private Coroutine _fade;

		private void Awake()
		{
			_source = GetComponent<AudioSource>();
			_source.playOnAwake   = false;
			_source.loop          = true;
			_source.spatialBlend  = 1f;
			_source.clip          = SirenClip;
			_source.volume        = 0f;
		}

		private void OnEnable()
		{
			MatchManager.PhaseChanged += OnPhaseChanged;
			// If the match object already exists (we enabled after it spawned), apply the current phase now —
			// PhaseChanged won't fire again until the next transition.
			if (MatchManager.Instance != null)
				OnPhaseChanged(MatchManager.Instance.Phase);
		}

		private void OnDisable()
		{
			MatchManager.PhaseChanged -= OnPhaseChanged;
		}

		private void OnPhaseChanged(MatchPhase phase)
		{
			// PhaseChanged replays the current phase for late-joiners and can repeat — only act on a real change.
			if (_hasApplied && phase == _appliedPhase) return;
			_appliedPhase = phase;
			_hasApplied   = true;

			if (phase == MatchPhase.DuskWarning)
			{
				StartSiren();
				float untilNight = MatchManager.Instance != null ? MatchManager.Instance.RemainingPhaseSeconds : 0f;
				WarningRaised?.Invoke(WarningMessage, untilNight);
			}
			else
			{
				StopSiren();
				WarningCleared?.Invoke();
			}
		}

		private void StartSiren()
		{
			if (SirenClip == null) return;
			if (_source.clip != SirenClip) _source.clip = SirenClip;
			if (_source.isPlaying == false) _source.Play();
			BeginFade(Volume, FadeInSeconds, stopWhenSilent: false);
		}

		private void StopSiren()
		{
			if (_source.isPlaying == false) return;
			BeginFade(0f, FadeOutSeconds, stopWhenSilent: true);
		}

		private void BeginFade(float target, float duration, bool stopWhenSilent)
		{
			if (_fade != null) StopCoroutine(_fade);
			if (isActiveAndEnabled == false || duration <= 0f)
			{
				_source.volume = target;
				if (stopWhenSilent && target <= 0f) _source.Stop();
				return;
			}
			_fade = StartCoroutine(FadeRoutine(target, duration, stopWhenSilent));
		}

		private IEnumerator FadeRoutine(float target, float duration, bool stopWhenSilent)
		{
			float start   = _source.volume;
			float elapsed = 0f;
			while (elapsed < duration)
			{
				elapsed       += Time.unscaledDeltaTime;
				_source.volume = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
				yield return null;
			}
			_source.volume = target;
			if (stopWhenSilent && target <= 0f) _source.Stop();
			_fade = null;
		}
	}
}
