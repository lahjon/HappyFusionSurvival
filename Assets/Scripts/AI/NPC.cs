using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Shared base for networked NPCs (hostile or friendly). Owns the common
	/// pieces every NPC needs: Health/NetworkTransform refs, IKnockbackable
	/// implementation with networked knockback fields, and a FixedUpdateNetwork
	/// skeleton that routes to alive/dead hooks and then applies knockback.
	///
	/// Subclasses override <see cref="OnFixedUpdateAlive"/> for behavior while
	/// alive; <see cref="OnFixedUpdateDead"/> is virtual (no-op default).
	/// </summary>
	public abstract class NPC : NetworkBehaviour, IKnockbackable
	{
		[Header("References")]
		public Health Health;
		public NetworkTransform NetworkTransform;

		[Header("Incoming Knockback")]
		[Tooltip("Constant deceleration (m/s²) applied to incoming knockback. Higher = snappier push. Duration is derived as sqrt(2 * distance / deceleration).")]
		public float KnockbackDeceleration = 20f;

		[Networked]
		private Vector3 _knockbackDirection { get; set; }
		[Networked]
		private float _knockbackInitialSpeed { get; set; }
		[Networked]
		private float _knockbackDuration { get; set; }
		[Networked]
		private TickTimer _knockbackTimer { get; set; }

		// NPCs without a Health component are non-damageable and always considered alive
		// (friendly town NPCs — shopkeeper, quest giver). Add a Health component for
		// anything that can be killed (chickens, hostile mobs).
		protected bool IsAlive => Health == null || Health.IsAlive;

		public sealed override void FixedUpdateNetwork()
		{
			if (Health != null && Health.IsAlive == false)
			{
				OnFixedUpdateDead();
				return;
			}

			OnFixedUpdateAlive();

			Vector3 knockback = ComputeKnockbackVelocity();
			if (knockback != Vector3.zero)
			{
				transform.position += knockback * Runner.DeltaTime;
			}
		}

		protected abstract void OnFixedUpdateAlive();
		protected virtual void OnFixedUpdateDead() { }

		public void ApplyKnockback(Vector3 fromPosition, float distance)
		{
			if (HasStateAuthority == false) return;
			if (distance <= 0f || KnockbackDeceleration <= 0f) return;

			float peakSpeed = Mathf.Sqrt(2f * KnockbackDeceleration * distance);
			float duration = peakSpeed / KnockbackDeceleration;

			Vector3 dir = transform.position - fromPosition;
			dir.y = 0f;
			if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
			dir.Normalize();

			_knockbackDirection = dir;
			_knockbackDuration = duration;
			_knockbackInitialSpeed = peakSpeed;
			_knockbackTimer = TickTimer.CreateFromSeconds(Runner, duration);
		}

		private Vector3 ComputeKnockbackVelocity()
		{
			if (_knockbackDuration <= 0f) return Vector3.zero;
			float? remaining = _knockbackTimer.RemainingTime(Runner);
			if (remaining == null || remaining.Value <= 0f) return Vector3.zero;
			float t = Mathf.Clamp01(remaining.Value / _knockbackDuration);
			return _knockbackDirection * (_knockbackInitialSpeed * t);
		}
	}
}
