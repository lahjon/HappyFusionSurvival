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

		[Header("Electricity")]
		[Tooltip("How fast the light fades when its grid zone loses/regains power (units per second). Higher = snappier.")]
		[Min(0.1f)] [SerializeField] private float _powerFadeSpeed = 4f;

		// Phase look (the target the light wants to show), kept separate so the zone-power multiplier can scale it.
		private Color _phaseColor;
		private float _phaseIntensity;

		// Zone power: _zonePower is the grid target (0..1); _displayedPower eases toward it to avoid a hard pop.
		private float _zonePower     = 1f;
		private float _displayedPower = 1f;

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
			LightGrid.PowerChanged    += OnPowerChanged;

			// Snap to the current grid power so a light that spawns in an already-dark zone doesn't fade in from lit.
			_zonePower = _displayedPower = ReadZonePower();

			// Apply the current phase right away — covers being enabled after MatchManager has already spawned,
			// and late-joiners (MatchManager re-fires PhaseChanged on Spawned, but Instance may already exist here).
			Apply(MatchManager.Instance != null ? MatchManager.Instance.Phase : MatchPhase.Lobby);
		}

		private void OnDisable()
		{
			MatchManager.PhaseChanged -= Apply;
			LightGrid.PowerChanged    -= OnPowerChanged;
		}

		private void Update()
		{
			// Ease the displayed power toward the zone target, then re-apply the look scaled by it.
			if (!Mathf.Approximately(_displayedPower, _zonePower))
			{
				_displayedPower = Mathf.MoveTowards(_displayedPower, _zonePower, _powerFadeSpeed * Time.deltaTime);
				ApplyLook();
			}
		}

		private void OnPowerChanged()
		{
			_zonePower = ReadZonePower();
		}

		/// <summary>Power for this light's zone, or 1 when there is no grid in the scene (behaves exactly as before).</summary>
		private float ReadZonePower()
		{
			return LightGrid.Instance != null ? LightGrid.Instance.GetZonePowerAt(transform.position) : 1f;
		}

		private void Apply(MatchPhase phase)
		{
			switch (phase)
			{
				case MatchPhase.DuskWarning:
					// Red is already creeping in and the bulbs are giving out.
					SetPhaseLook(_nightColor, _nightIntensity);
					SetFlicker(_flickerOnDusk);
					break;

				case MatchPhase.Night:
					SetPhaseLook(_nightColor, _nightIntensity);
					SetFlicker(_flickerAtNight);
					break;

				case MatchPhase.MatchOver:
					// Freeze whatever was current — don't yank players out of the mood.
					break;

				default: // Lobby, Day
					SetPhaseLook(_dayColor, _dayIntensity);
					SetFlicker(false);
					break;
			}
		}

		/// <summary>Record the phase's target colour/intensity, then push the current look (scaled by zone power).</summary>
		private void SetPhaseLook(Color color, float intensity)
		{
			_phaseColor     = color;
			_phaseIntensity = intensity;
			ApplyLook();
		}

		/// <summary>Drive the light from the stored phase look multiplied by the (eased) zone power. At zero power the
		/// light — and its flickering emissive bulb — goes fully dark.</summary>
		private void ApplyLook()
		{
			if (_light != null)
				_light.color = _phaseColor;

			float intensity = _phaseIntensity * _displayedPower;

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
