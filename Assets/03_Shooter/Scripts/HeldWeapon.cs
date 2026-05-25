using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Authoring component on a weapon's HandPrefab. Marks the prefab as a usable weapon
	/// and carries tuning that Player.Fire reads — range, damage, and (for melee) a
	/// procedural swing animation. Inventory moves the whole instance to the
	/// FirstPersonOverlay layer when held by the local player.
	/// </summary>
	public sealed class HeldWeapon : MonoBehaviour
	{
		[Header("Combat")]
		[Tooltip("Hitscan range in meters. Ranged weapons (pistol) use ~200; melee weapons (bat) use ~2.")]
		public float Range = 200f;
		[Tooltip("Damage dealt per hit.")]
		public int Damage = 1;
		[Tooltip("If true, Fire triggers Swing() instead of muzzle FX; no fire sound is played.")]
		public bool IsMelee = false;

		[Header("Ranged FX")]
		[Tooltip("Plays when the weapon fires. Ranged only.")]
		public ParticleSystem MuzzleParticle;

		[Header("Audio")]
		[Tooltip("Sound played on attack (gunshot for ranged, whoosh/impact for melee). Lives on the held visual so audio is spatial to the weapon, not the player root.")]
		public AudioClip AttackClip;
		[Tooltip("Optional AudioSource on the held visual. If left empty, one is auto-added at runtime configured for 3D playback.")]
		public AudioSource AttackSource;
		[Range(0f, 1f)] public float AttackVolume = 1f;

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

		[Header("Knockback (melee only)")]
		[Tooltip("Distance (meters) the target is pushed away from the attacker. Ignored for non-melee weapons. 0 = no knockback. Duration is derived on the receiver from its KnockbackDeceleration.")]
		public float KnockbackDistance = 2f;

		[Header("Fire Rate")]
		[Tooltip("Minimum seconds between attacks with this weapon. 0 = no cooldown.")]
		public float Cooldown = 0.5f;

		[Header("Recoil (ranged only)")]
		[Tooltip("Distance (meters) the weapon kicks backward along local -Z at peak. Ignored for melee.")]
		public float RecoilBackKick = 0.06f;
		[Tooltip("Muzzle-up pitch (degrees, around local X). Negative tilts the muzzle up — that's the usual sign for recoil. Ignored for melee.")]
		public float RecoilPitchDegrees = -8f;
		[Tooltip("Total recoil duration in seconds (kick out + return).")]
		public float RecoilDuration = 0.12f;

		private float _t = -1f;
		private float _recoilT = -1f;
		private Quaternion _restRotation;
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

		/// <summary>Kicks the weapon back + up briefly. Called by Player.ShowFireEffects on every peer when a ranged shot lands; no-op for melee.</summary>
		public void Recoil()
		{
			if (IsMelee) return;
			_recoilT = 0f;
		}

		/// <summary>Plays <see cref="AttackClip"/> on the local AudioSource. Called by Player.ShowFireEffects.</summary>
		public void PlayAttackSound()
		{
			if (AttackClip == null || AttackSource == null) return;
			AttackSource.PlayOneShot(AttackClip, AttackVolume);
		}

		public void Swing(bool charged = false)
		{
			if (!IsMelee) return;
			if (!_restCaptured)
			{
				_restRotation = transform.localRotation;
				_restCaptured = true;
			}
			_t = 0f;
			_activeSwingArcScale = charged ? ChargedSwingArcScale : 1f;
			_activeSwingDurationScale = charged ? ChargedSwingDurationScale : 1f;
			// Swing takes over from the charge pose.
			_isCharging = false;
			_chargeProgress = 0f;
			_chargePoseT = 0f;
		}

		public void SetCharging(bool charging, float progress)
		{
			if (!IsMelee) return;
			_isCharging = charging;
			_chargeProgress = charging ? Mathf.Clamp01(progress) : 0f;
		}

		private void Update()
		{
			// Swing animation takes priority over the charge pose.
			if (_t >= 0f)
			{
				_t += Time.deltaTime;
				float duration = Mathf.Max(0.01f, SwingDuration * _activeSwingDurationScale);
				float u = Mathf.Clamp01(_t / duration);
				float k = u < 0.5f ? u * 2f : (1f - u) * 2f;
				transform.localRotation = _restRotation * Quaternion.Euler(SwingArcEuler * _activeSwingArcScale * k);

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

			Vector3 backwardArc = -SwingArcEuler * ChargedBackMultiplier;
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
