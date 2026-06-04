using System;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Town-wide audible "the Purge is coming" warning. A local-only view of the networked match phase: every peer
	/// receives <see cref="MatchManager.PhaseChanged"/>, so the siren sounds identically on all clients without any
	/// networked state of its own (per the project rule that audio/UI cues are local reads of the single networked phase).
	///
	/// The siren is a non-looping one-shot that blasts twice across the <see cref="MatchPhase.DuskWarning"/> window:
	/// once the moment dusk begins, and once <see cref="SecondSirenLeadSeconds"/> seconds before Night — the second blast
	/// timed off <see cref="MatchManager.RemainingPhaseSeconds"/> (the networked dusk countdown) so every peer sounds it
	/// together. NPC retreat keys off the same DuskWarning transition.
	///
	/// Scene-placed at a town landmark (a tower/speaker). Plays through its own 3D AudioSource so it's positional; a HUD
	/// banner can subscribe to <see cref="WarningRaised"/> without coupling.
	/// </summary>
	[RequireComponent(typeof(AudioSource))]
	public sealed class WorldSiren : MonoBehaviour
	{
		[Header("Audio")]
		[Tooltip("One-shot siren clip, played on this component's own 3D AudioSource. Not looped.")]
		public AudioClip SirenClip;
		[Range(0f, 1f)] public float Volume = 1f;

		[Header("Timing")]
		[Tooltip("Seconds before Night begins that the second siren blast sounds, read from the match's DuskWarning " +
			"countdown. The first blast always fires the instant dusk starts.")]
		[Min(0f)] public float SecondSirenLeadSeconds = 10f;

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
		// Guards the once-per-dusk second blast; reset each time dusk begins.
		private bool _secondSirenPlayed;

		private void Awake()
		{
			_source = GetComponent<AudioSource>();
			_source.playOnAwake  = false;
			_source.loop         = false;
			_source.spatialBlend = 1f;
			_source.clip         = SirenClip;
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
				// First blast the instant the Purge warning begins; the second is armed for SecondSirenLeadSeconds out.
				_secondSirenPlayed = false;
				Sound();
				float untilNight = MatchManager.Instance != null ? MatchManager.Instance.RemainingPhaseSeconds : 0f;
				WarningRaised?.Invoke(WarningMessage, untilNight);
			}
			else
			{
				if (_source.isPlaying) _source.Stop();
				WarningCleared?.Invoke();
			}
		}

		private void Update()
		{
			// Second blast: a fixed lead-time before Night, read from the networked dusk countdown so it lands on the
			// same moment on every peer. One-shot per dusk.
			if (_appliedPhase != MatchPhase.DuskWarning || _secondSirenPlayed) return;
			var mm = MatchManager.Instance;
			if (mm == null || mm.Phase != MatchPhase.DuskWarning) return;
			if (mm.RemainingPhaseSeconds <= SecondSirenLeadSeconds)
			{
				_secondSirenPlayed = true;
				Sound();
			}
		}

		private void Sound()
		{
			if (SirenClip == null) return;
			if (_source.clip != SirenClip) _source.clip = SirenClip;
			_source.PlayOneShot(SirenClip, Volume);
		}
	}
}
