using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// A street light that reacts to the networked match phase: warm + steady through the Day "Town" phase,
	/// an unstable scary flicker during <see cref="MatchPhase.DuskWarning"/>, and an ominous red glow once the
	/// Purge (<see cref="MatchPhase.Night"/>) begins.
	///
	/// Cosmetic, so it is a plain <see cref="MonoBehaviour"/> — but it is still *phase-aware* per the project rule:
	/// it reads the phase from <see cref="MatchManager.PhaseChanged"/> (the single networked source of truth), never
	/// from local <c>Time.time</c>. That keeps the red flip and the flicker synced across every peer to within a tick.
	///
	/// Pair with a <see cref="LightFlicker"/> on the same object for the flicker effect; without one the colour/intensity
	/// changes still work, just without flicker.
	/// </summary>
	[RequireComponent(typeof(Light))]
	public sealed class PhaseStreetLight : MonoBehaviour
	{
		[Header("References (auto-found if null)")]
		[SerializeField] private Light _light;
		[SerializeField] private LightFlicker _flicker;

		[Header("Day — Town")]
		[SerializeField] private Color _dayColor = new Color(1f, 0.93f, 0.78f);
		[Min(0f)] [SerializeField] private float _dayIntensity = 2f;

		[Header("Night — Purge")]
		[SerializeField] private Color _nightColor = new Color(1f, 0.05f, 0.05f);
		[Min(0f)] [SerializeField] private float _nightIntensity = 3f;

		[Header("Flicker behaviour")]
		[Tooltip("Flicker hard during the DuskWarning transition (lights failing as the Purge approaches).")]
		[SerializeField] private bool _flickerOnDusk = true;
		[Tooltip("Keep a subtle flicker going through the whole Night phase.")]
		[SerializeField] private bool _flickerAtNight = false;

		private void Reset()
		{
			_light   = GetComponent<Light>();
			_flicker = GetComponent<LightFlicker>();
		}

		private void OnEnable()
		{
			if (_light == null)   _light   = GetComponent<Light>();
			if (_flicker == null) _flicker = GetComponent<LightFlicker>();

			MatchManager.PhaseChanged += Apply;

			// Apply the current phase right away — covers being enabled after MatchManager has already spawned,
			// and late-joiners (MatchManager re-fires PhaseChanged on Spawned, but Instance may already exist here).
			Apply(MatchManager.Instance != null ? MatchManager.Instance.Phase : MatchPhase.Lobby);
		}

		private void OnDisable()
		{
			MatchManager.PhaseChanged -= Apply;
		}

		private void Apply(MatchPhase phase)
		{
			switch (phase)
			{
				case MatchPhase.DuskWarning:
					// Red is already creeping in and the bulbs are giving out.
					SetLook(_nightColor, _nightIntensity);
					SetFlicker(_flickerOnDusk);
					break;

				case MatchPhase.Night:
					SetLook(_nightColor, _nightIntensity);
					SetFlicker(_flickerAtNight);
					break;

				case MatchPhase.MatchOver:
					// Freeze whatever was current — don't yank players out of the mood.
					break;

				default: // Lobby, Day
					SetLook(_dayColor, _dayIntensity);
					SetFlicker(false);
					break;
			}
		}

		private void SetLook(Color color, float intensity)
		{
			if (_light != null)
				_light.color = color;

			// LightFlicker owns intensity each frame, so route brightness through it when present.
			if (_flicker != null)
				_flicker.SetBaseIntensity(intensity);
			else if (_light != null)
				_light.intensity = intensity;
		}

		private void SetFlicker(bool on)
		{
			if (_flicker == null)
				return;
			if (on)
				_flicker.StartFlicker();
			else
				_flicker.StopFlicker();
		}
	}
}
