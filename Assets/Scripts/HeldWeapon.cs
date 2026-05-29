using System.Collections.Generic;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Authoring component on a weapon's HandPrefab. Carries the list of CombatActions
	/// the weapon can perform (most weapons use just <see cref="Actions"/>[0]) plus
	/// the visual/animation tuning for ranged kick or melee swing. Inventory moves
	/// the whole instance to the FirstPersonOverlay layer when held by the local player.
	/// </summary>
	public sealed class HeldWeapon : MonoBehaviour, IActionProvider
	{
		[Header("Combat")]
		[Tooltip("Ordered list of actions this weapon can perform. Most weapons have one; the first is used by default.")]
		[SerializeField] private List<CombatAction> _actions = new List<CombatAction>();

		public IReadOnlyList<CombatAction> Actions => _actions;

		[Header("Audio")]
		[Tooltip("Optional AudioSource on the held visual. If left empty, one is auto-added at runtime configured for 3D playback.")]
		public AudioSource AttackSource;

		[Header("Ranged FX")]
		[Tooltip("Plays when the weapon fires. Used when the fired action's Style is Ranged.")]
		public ParticleSystem MuzzleParticle;

		[Header("Melee Swing")]
		[Tooltip("Local-space rotation arc at the peak of the swing (in euler degrees).")]
		public Vector3 SwingArcEuler = new Vector3(-80f, 0f, 0f);
		[Tooltip("Total duration of the forward-and-back swing in seconds.")]
		public float SwingDuration = 0.28f;

		[Header("Charged Attack Visuals")]
		[Tooltip("How far backward the weapon is pulled while charging. Expressed as a multiplier of the mirrored SwingArcEuler.")]
		[Range(0f, 1.5f)] public float ChargedBackMultiplier = 0.8f;
		[Tooltip("Smoothing speed for easing into/out of the charge pose.")]
		public float ChargePoseLerpSpeed = 12f;
		[Tooltip("Arc multiplier when releasing a fully-charged swing — sells the heavier hit.")]
		public float ChargedSwingArcScale = 1.35f;
		[Tooltip("Duration multiplier when releasing a fully-charged swing.")]
		public float ChargedSwingDurationScale = 1.2f;

		[Header("Recoil (ranged only)")]
		[Tooltip("Distance (meters) the weapon kicks backward along local -Z at peak.")]
		public float RecoilBackKick = 0.06f;
		[Tooltip("Muzzle-up pitch (degrees, around local X). Negative tilts the muzzle up — that's the usual sign for recoil.")]
		public float RecoilPitchDegrees = -8f;
		[Tooltip("Total recoil duration in seconds (kick out + return).")]
		public float RecoilDuration = 0.12f;

		[Header("Aim Sway (ranged only)")]
		[Tooltip("Slight oscillating wobble of the aim that makes it harder to keep on target. 0 = no sway, 1 = max sway.")]
		[Range(0f, 1f)] public float WeaponSway = 0f;
		[Tooltip("Max sway amplitude in degrees when WeaponSway = 1.")]
		public float SwayMaxDegrees = 1.25f;
		[Tooltip("Sway oscillation frequency in Hz. Lower = slower wobble.")]
		public float SwayFrequency = 0.9f;

		[Header("Aim Recoil (ranged only, CS-style)")]
		[Tooltip("Per-shot recoil kick applied to the camera/crosshair. 0 = no recoil, 1 = max. Aim is lerped toward the new offset; the kick is sticky until the player compensates.")]
		[Range(0f, 1f)] public float AimRecoil = 0f;
		[Tooltip("Vertical pitch-up (degrees) per shot at AimRecoil = 1.")]
		public float AimRecoilPitchPerShot = 3.5f;
		[Tooltip("Horizontal random spread (± degrees) per shot at AimRecoil = 1.")]
		public float AimRecoilHorizontalRandom = 1.5f;
		[Tooltip("How fast the aim lerps to the new recoil target. Larger = snappier kick.")]
		public float AimRecoilLerpSpeed = 18f;

		private float _t = -1f;
		private float _recoilT = -1f;
		private Quaternion _restRotation;
		private Quaternion _swingStartRotation;
		private Vector3 _restPosition;
		private bool _restCaptured;
		private bool _isCharging;
		private float _chargeProgress;
		private float _chargePoseT;
		private float _activeSwingArcScale = 1f;
		private float _activeSwingDurationScale = 1f;

		private void OnEnable()
		{
			if (AttackSource == null)
			{
				AttackSource = GetComponent<AudioSource>();
				if (AttackSource == null)
				{
					AttackSource = gameObject.AddComponent<AudioSource>();
					AttackSource.playOnAwake = false;
					AttackSource.spatialBlend = 1f;
					AttackSource.minDistance = 1f;
					AttackSource.maxDistance = 25f;
				}
			}
		}

		private void Start()
		{
			// Inventory instantiates the prefab and THEN resets localRotation to identity.
			// OnEnable fires inside Instantiate (before that reset), so it would capture the
			// prefab root's original rotation — for Hand_Pistol that's a -90° Y twist, which
			// the per-frame "force back to rest" line in Update would then re-apply forever.
			// Start runs on the next frame, after Inventory has applied the reset.
			_restRotation = transform.localRotation;
			_restPosition = transform.localPosition;
			_restCaptured = true;
		}

		public void PlayAttackSound(CombatAction action)
		{
			if (action == null || action.AttackClip == null || AttackSource == null) return;
			AttackSource.PlayOneShot(action.AttackClip, action.AttackVolume);
		}

		public void PlayMeleeFeedback(bool charged) => Swing(charged);

		public void PlayRangedFeedback()
		{
			if (MuzzleParticle != null) MuzzleParticle.Play();
			Recoil();
		}

		public void SetCharging(bool charging, float progress)
		{
			_isCharging = charging;
			_chargeProgress = charging ? Mathf.Clamp01(progress) : 0f;
		}

		private void Swing(bool charged)
		{
			if (!_restCaptured)
			{
				_restRotation = transform.localRotation;
				_restCaptured = true;
			}
			// Capture where the bat is right now (rest or mid-charge) so the strike
			// animates from there rather than snapping back to rest first.
			_swingStartRotation = transform.localRotation;
			_t = 0f;
			_activeSwingArcScale = charged ? ChargedSwingArcScale : 1f;
			_activeSwingDurationScale = charged ? ChargedSwingDurationScale : 1f;
			_isCharging = false;
			_chargeProgress = 0f;
			_chargePoseT = 0f;
		}

		private void Recoil() => _recoilT = 0f;

		private void Update()
		{
			// Swing animation takes priority over the charge pose.
			if (_t >= 0f)
			{
				_t += Time.deltaTime;
				float duration = Mathf.Max(0.01f, SwingDuration * _activeSwingDurationScale);
				float u = Mathf.Clamp01(_t / duration);

				// First 40%: snap from wherever the bat is (rest or charge pose) to the strike peak.
				// Last 60%: return smoothly to rest.
				var strikePeak = _restRotation * Quaternion.Euler(-SwingArcEuler * _activeSwingArcScale);
				if (u < 0.4f)
					transform.localRotation = Quaternion.Lerp(_swingStartRotation, strikePeak, Mathf.SmoothStep(0f, 1f, u / 0.4f));
				else
					transform.localRotation = Quaternion.Lerp(strikePeak, _restRotation, Mathf.SmoothStep(0f, 1f, (u - 0.4f) / 0.6f));

				if (u >= 1f)
				{
					_t = -1f;
					_activeSwingArcScale = 1f;
					_activeSwingDurationScale = 1f;
					transform.localRotation = _restRotation;
				}
				return;
			}

			if (!_restCaptured) return;

			float target = _isCharging ? _chargeProgress : 0f;
			float a = 1f - Mathf.Exp(-Mathf.Max(0f, ChargePoseLerpSpeed) * Time.deltaTime);
			_chargePoseT = Mathf.Lerp(_chargePoseT, target, a);

			if (_chargePoseT < 0.001f && !_isCharging)
			{
				ApplyRecoilOrRest();
				return;
			}

			Vector3 backwardArc = SwingArcEuler * ChargedBackMultiplier;
			transform.localRotation = _restRotation * Quaternion.Euler(backwardArc * _chargePoseT);
		}

		// Ranged weapons never swing/charge, so they always reach the rest-pose branch above.
		// Layer recoil onto the rest pose here: triangular kick envelope (out → peak → return),
		// with a backward translation along local -Z and a small muzzle-up pitch around local X.
		private void ApplyRecoilOrRest()
		{
			if (_recoilT < 0f)
			{
				transform.localRotation = _restRotation;
				transform.localPosition = _restPosition;
				return;
			}

			_recoilT += Time.deltaTime;
			float duration = Mathf.Max(0.01f, RecoilDuration);
			float u = Mathf.Clamp01(_recoilT / duration);
			float k = u < 0.5f ? u * 2f : (1f - u) * 2f;

			transform.localPosition = _restPosition + new Vector3(0f, 0f, -RecoilBackKick * k);
			transform.localRotation = _restRotation * Quaternion.Euler(RecoilPitchDegrees * k, 0f, 0f);

			if (u >= 1f)
			{
				_recoilT = -1f;
				transform.localPosition = _restPosition;
				transform.localRotation = _restRotation;
			}
		}
	}
}
