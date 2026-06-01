using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Visual rig on the Fist hand prefab: animates the fist forward-back when the held fire counter
	/// advances. The unarmed punch CombatAction now lives on the off-catalog "Unarmed" item asset's
	/// <see cref="WeaponCapability"/> (wired into Inventory._unarmedItem); this component only reacts
	/// via <see cref="IHeldVisual"/>. Same role as <see cref="HeldWeapon"/> for fists.
	/// </summary>
	public sealed class FistPunchAnimator : MonoBehaviour, IHeldVisual
	{
		[Header("Audio")]
		[Tooltip("Optional AudioSource. If left empty, one is auto-added at runtime configured for 3D playback.")]
		public AudioSource AttackSource;

		[Header("Punch Animation")]
		[Tooltip("Local-space offset the fist travels at the peak of the punch.")]
		public Vector3 PunchOffset = new Vector3(0f, 0f, 0.3f);
		[Tooltip("Total duration of the forward-and-back punch animation in seconds.")]
		public float PunchDuration = 0.18f;

		[Header("Charged Attack Visuals")]
		[Tooltip("How far backward (along -PunchOffset) the fist is pulled while charging.")]
		[Range(0f, 1.5f)] public float ChargedBackMultiplier = 0.6f;
		[Tooltip("Smoothing speed for easing into/out of the charge pose.")]
		public float ChargePoseLerpSpeed = 12f;
		[Tooltip("Offset multiplier when releasing a fully-charged punch.")]
		public float ChargedPunchScale = 1.4f;
		[Tooltip("Duration multiplier when releasing a fully-charged punch.")]
		public float ChargedDurationScale = 1.2f;

		private float _t = -1f;
		private Vector3 _restPosition;
		private bool _restCaptured;
		private bool _isCharging;
		private float _chargeProgress;
		private float _chargePoseT;
		private float _activeOffsetScale = 1f;
		private float _activeDurationScale = 1f;

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
			_restPosition = transform.localPosition;
			_restCaptured = true;
		}

		public void PlayAttackSound(CombatAction action)
		{
			if (action == null || action.AttackClip == null || AttackSource == null) return;
			AttackSource.PlayOneShot(action.AttackClip, action.AttackVolume);
		}

		public void PlayMeleeFeedback(bool charged) => Punch(charged);

		// Fists are inherently melee; if a ranged action is somehow attached, fall back to a punch.
		public void PlayRangedFeedback() => Punch(false);

		public void SetCharging(bool charging, float progress)
		{
			_isCharging = charging;
			_chargeProgress = charging ? Mathf.Clamp01(progress) : 0f;
		}

		private void Punch(bool charged)
		{
			_t = 0f;
			_activeOffsetScale = charged ? ChargedPunchScale : 1f;
			_activeDurationScale = charged ? ChargedDurationScale : 1f;
			_isCharging = false;
			_chargeProgress = 0f;
			_chargePoseT = 0f;
		}

		private void Update()
		{
			if (_t >= 0f)
			{
				_t += Time.deltaTime;
				float duration = Mathf.Max(0.01f, PunchDuration * _activeDurationScale);
				float u = Mathf.Clamp01(_t / duration);
				float k = u < 0.5f ? u * 2f : (1f - u) * 2f;
				transform.localPosition = _restPosition + PunchOffset * _activeOffsetScale * k;

				if (u >= 1f)
				{
					_t = -1f;
					_activeOffsetScale = 1f;
					_activeDurationScale = 1f;
					transform.localPosition = _restPosition;
				}
				return;
			}

			if (!_restCaptured) return;

			float target = _isCharging ? _chargeProgress : 0f;
			float a = 1f - Mathf.Exp(-Mathf.Max(0f, ChargePoseLerpSpeed) * Time.deltaTime);
			_chargePoseT = Mathf.Lerp(_chargePoseT, target, a);

			if (_chargePoseT < 0.001f && !_isCharging)
			{
				transform.localPosition = _restPosition;
				return;
			}

			transform.localPosition = _restPosition - PunchOffset * ChargedBackMultiplier * _chargePoseT;
		}
	}
}
