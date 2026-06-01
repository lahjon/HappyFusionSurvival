using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Visual rig on a weapon's HandPrefab: the audio/particle refs and the animation tuning for
	/// ranged kick or melee swing. The weapon's actual CombatActions now live on the item asset's
	/// <see cref="WeaponCapability"/>; this component only reacts via <see cref="IHeldVisual"/> when
	/// the holder fires. Inventory moves the whole instance to the FirstPersonOverlay layer when
	/// held by the local player.
	/// </summary>
	public sealed class HeldWeapon : MonoBehaviour, IHeldVisual
	{
		[Header("Rig refs (Hand_Generic)")]
		[Tooltip("Child holding the in-hand mesh — configured from the item's ItemVisual at equip.")]
		[SerializeField] private Transform _meshChild;
		[Tooltip("Muzzle-flash particle on the MuzzleAnchor child; positioned + enabled per weapon at equip.")]
		[SerializeField] private ParticleSystem _muzzle;

		[Tooltip("Optional AudioSource on the held visual. If left empty, one is auto-added at runtime configured for 3D playback.")]
		public AudioSource AttackSource;

		// Runtime tuning, copied from the held item's WeaponCapability.Rig in Configure(). Public so
		// PlayerInput can read sway/aim-recoil; NOT serialized — the authored values live on the asset.
		[System.NonSerialized] public Vector3 SwingArcEuler = new Vector3(-80f, 0f, 0f);
		[System.NonSerialized] public float SwingDuration = 0.28f;
		[System.NonSerialized] public float ChargedBackMultiplier = 0.8f;
		[System.NonSerialized] public float ChargePoseLerpSpeed = 12f;
		[System.NonSerialized] public float ChargedSwingArcScale = 1.35f;
		[System.NonSerialized] public float ChargedSwingDurationScale = 1.2f;
		[System.NonSerialized] public float RecoilBackKick = 0.06f;
		[System.NonSerialized] public float RecoilPitchDegrees = -8f;
		[System.NonSerialized] public float RecoilDuration = 0.12f;
		[System.NonSerialized] public float WeaponSway = 0f;
		[System.NonSerialized] public float SwayMaxDegrees = 1.25f;
		[System.NonSerialized] public float SwayFrequency = 0.9f;
		[System.NonSerialized] public float AimRecoil = 0f;
		[System.NonSerialized] public float AimRecoilPitchPerShot = 3.5f;
		[System.NonSerialized] public float AimRecoilHorizontalRandom = 1.5f;
		[System.NonSerialized] public float AimRecoilLerpSpeed = 18f;

		private ParticleSystem _muzzleParticle;

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
			if (_muzzleParticle != null) _muzzleParticle.Play();
			Recoil();
		}

		/// <summary>
		/// Equip-time setup for the generic hand rig: build the held mesh from <paramref name="visual"/>
		/// and copy the swing/recoil/sway tuning + muzzle from the held item's <paramref name="weapon"/>
		/// capability. Called by Inventory.RefreshHeldItem right after instantiation. A null
		/// <paramref name="weapon"/> (non-weapon held item) leaves the rig at rest with no muzzle.
		/// </summary>
		public void Configure(ItemVisual visual, WeaponCapability weapon)
		{
			if (_meshChild != null && visual != null)
			{
				if (visual.VisualOverride != null)
				{
					Instantiate(visual.VisualOverride, _meshChild);
					if (_meshChild.TryGetComponent<MeshRenderer>(out var pr)) pr.enabled = false;
				}
				else
				{
					if (visual.Mesh != null && _meshChild.TryGetComponent<MeshFilter>(out var mf))
						mf.sharedMesh = visual.Mesh;
					if (_meshChild.TryGetComponent<MeshRenderer>(out var mr))
					{
						if (visual.Material != null) mr.sharedMaterial = visual.Material;
						mr.enabled = true;
					}
				}
				_meshChild.localScale = visual.HeldScale;
				_meshChild.localPosition = visual.HeldLocalPosition;
				_meshChild.localRotation = Quaternion.Euler(visual.HeldLocalEuler);
			}

			var r = weapon?.Rig;
			if (r != null)
			{
				SwingArcEuler = r.SwingArcEuler;
				SwingDuration = r.SwingDuration;
				ChargedBackMultiplier = r.ChargedBackMultiplier;
				ChargePoseLerpSpeed = r.ChargePoseLerpSpeed;
				ChargedSwingArcScale = r.ChargedSwingArcScale;
				ChargedSwingDurationScale = r.ChargedSwingDurationScale;
				RecoilBackKick = r.RecoilBackKick;
				RecoilPitchDegrees = r.RecoilPitchDegrees;
				RecoilDuration = r.RecoilDuration;
				WeaponSway = r.WeaponSway;
				SwayMaxDegrees = r.SwayMaxDegrees;
				SwayFrequency = r.SwayFrequency;
				AimRecoil = r.AimRecoil;
				AimRecoilPitchPerShot = r.AimRecoilPitchPerShot;
				AimRecoilHorizontalRandom = r.AimRecoilHorizontalRandom;
				AimRecoilLerpSpeed = r.AimRecoilLerpSpeed;
			}

			// Hide the muzzle anchor entirely for weapons without a muzzle. Hand_Generic is built from
			// the pistol, so it always carries the pistol's muzzle particle child — without this, a
			// throwing knife / bat would show a stray bit of the pistol's muzzle flash.
			bool useMuzzle = r != null && r.HasMuzzle;
			if (_muzzle != null)
			{
				if (useMuzzle) _muzzle.transform.localPosition = r.MuzzleLocalPosition;
				_muzzle.gameObject.SetActive(useMuzzle);
			}
			_muzzleParticle = useMuzzle ? _muzzle : null;
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
