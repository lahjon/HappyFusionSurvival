using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Reusable, purely-cosmetic "scary" flicker for a <see cref="Light"/> (and an optional emissive bulb renderer).
	///
	/// Local-only by design — flicker is visual noise, so every peer runs its own instance with its own random seed
	/// (lights flickering in lock-step looks fake). Anything that should be *synced* — e.g. "all streetlights start
	/// flickering at DuskWarning" — is driven by toggling <see cref="StartFlicker"/>/<see cref="StopFlicker"/> from a
	/// phase-aware controller (see <see cref="PhaseStreetLight"/>) that itself reads the networked phase. This component
	/// never reads networked state and never touches <c>Time.time</c> for gameplay — just <c>Time.deltaTime</c> for view.
	///
	/// Owns the light's <c>intensity</c> every frame: when not flickering it holds the light steady at the base
	/// intensity, so a controller sets <see cref="SetBaseIntensity"/> + <c>Light.color</c> and lets this drive the rest.
	/// </summary>
	[RequireComponent(typeof(Light))]
	public sealed class LightFlicker : MonoBehaviour
	{
		[Header("Target")]
		[SerializeField] private Light _light;
		[Tooltip("Optional bulb/fixture renderer whose emission tracks the flicker (goes dark on blackouts).")]
		[SerializeField] private Renderer _emissiveRenderer;
		[SerializeField] private string _emissionColorProperty = "_EmissionColor";

		[Header("Smooth flutter")]
		[Tooltip("Lowest the light dips to during normal flutter, as a fraction of base intensity.")]
		[Range(0f, 1f)] [SerializeField] private float _minIntensity = 0.35f;
		[Tooltip("Brightest the flutter reaches, as a fraction of base intensity.")]
		[Range(0f, 1f)] [SerializeField] private float _maxIntensity = 1f;
		[Tooltip("How nervous the flutter is. Higher = faster buzzing.")]
		[SerializeField] private float _flutterSpeed = 18f;

		[Header("Dropouts (the scary part)")]
		[Tooltip("Average seconds between hard blackouts.")]
		[SerializeField] private float _dropoutInterval = 4f;
		[Tooltip("+/- randomisation on the interval so blackouts feel unpredictable.")]
		[SerializeField] private float _dropoutIntervalJitter = 3f;
		[Tooltip("How long a blackout lasts.")]
		[SerializeField] private float _dropoutDuration = 0.12f;
		[SerializeField] private float _dropoutDurationJitter = 0.18f;
		[Tooltip("Number of rapid on/off stutters as the light struggles back to life after a blackout.")]
		[SerializeField] private int _recoveryStutters = 4;

		[Header("Startup")]
		[Tooltip("Begin flickering immediately (for standalone horror props with no phase controller).")]
		[SerializeField] private bool _flickerOnStart = false;

		/// <summary>True while the scary flicker is active. When false the light holds steady at base intensity.</summary>
		public bool IsFlickering { get; private set; }

		private float _baseIntensity = 1f;
		private float _noiseSeed;

		// Dropout state machine.
		private float _dropoutTimer;   // counts down to the next blackout
		private float _blackoutTimer;  // > 0 while fully dark
		private int   _stutterCount;   // remaining recovery stutters after a blackout
		private float _stutterTimer;   // time until the next stutter toggle

		// Emission.
		private MaterialPropertyBlock _mpb;
		private int _emissionId;
		private Color _baseEmission;
		private bool _hasEmission;

		private void Awake()
		{
			if (_light == null)
				_light = GetComponent<Light>();
			_baseIntensity = _light != null ? _light.intensity : 1f;

			// Per-instance seed so neighbouring lights don't flutter in sync.
			_noiseSeed = Random.value * 1000f;

			_emissionId = Shader.PropertyToID(_emissionColorProperty);
			if (_emissiveRenderer != null)
			{
				var mat = _emissiveRenderer.sharedMaterial;
				if (mat != null && mat.HasProperty(_emissionId))
				{
					_baseEmission = mat.GetColor(_emissionId);
					_mpb = new MaterialPropertyBlock();
					_hasEmission = true;
				}
			}

			ResetDropoutState();
		}

		private void Start()
		{
			if (_flickerOnStart)
				StartFlicker();
		}

		/// <summary>Begin the scary flicker.</summary>
		public void StartFlicker()
		{
			if (IsFlickering)
				return;
			IsFlickering = true;
			ResetDropoutState();
		}

		/// <summary>Stop flickering and hold the light steady at the current base intensity.</summary>
		public void StopFlicker()
		{
			IsFlickering = false;
		}

		/// <summary>Set the intensity the light returns to when fully "on". Lets a controller change brightness per phase.</summary>
		public void SetBaseIntensity(float intensity)
		{
			_baseIntensity = Mathf.Max(0f, intensity);
		}

		private void ResetDropoutState()
		{
			_dropoutTimer  = NextDropoutDelay();
			_blackoutTimer = 0f;
			_stutterCount  = 0;
			_stutterTimer  = 0f;
		}

		private float NextDropoutDelay()
		{
			return Mathf.Max(0.25f, _dropoutInterval + Random.Range(-_dropoutIntervalJitter, _dropoutIntervalJitter));
		}

		private void Update()
		{
			if (_light == null)
				return;

			float output = IsFlickering ? EvaluateFlicker() : 1f;

			_light.intensity = _baseIntensity * output;

			if (_hasEmission)
			{
				_emissiveRenderer.GetPropertyBlock(_mpb);
				_mpb.SetColor(_emissionId, _baseEmission * output);
				_emissiveRenderer.SetPropertyBlock(_mpb);
			}
		}

		/// <summary>Returns the current 0..1 intensity multiplier for the flicker effect.</summary>
		private float EvaluateFlicker()
		{
			// Schedule the next blackout, but only once any current blackout + recovery has finished.
			_dropoutTimer -= Time.deltaTime;
			if (_dropoutTimer <= 0f && _blackoutTimer <= 0f && _stutterCount <= 0)
			{
				_blackoutTimer = Mathf.Max(0.02f, _dropoutDuration + Random.Range(-_dropoutDurationJitter, _dropoutDurationJitter));
				_stutterCount  = Mathf.Max(0, _recoveryStutters);
				_dropoutTimer  = NextDropoutDelay();
			}

			if (_blackoutTimer > 0f)
			{
				_blackoutTimer -= Time.deltaTime;
				return 0f; // fully dark
			}

			if (_stutterCount > 0)
			{
				_stutterTimer -= Time.deltaTime;
				if (_stutterTimer <= 0f)
				{
					_stutterTimer = Random.Range(0.03f, 0.09f);
					_stutterCount--;
				}
				// Alternate near-dark / full as it struggles back on.
				return (_stutterCount & 1) == 0 ? 1f : 0.05f;
			}

			// Smooth nervous flutter via Perlin noise (no hard steps).
			float n = Mathf.PerlinNoise(_noiseSeed, Time.time * _flutterSpeed);
			return Mathf.Lerp(_minIntensity, _maxIntensity, n);
		}
	}
}
