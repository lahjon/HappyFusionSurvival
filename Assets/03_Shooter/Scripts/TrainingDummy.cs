using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Stationary practice enemy. Scans for players within PunchRange on the state
	/// authority's tick and, when cooldown elapses, punches the closest one by routing
	/// through Player.ApplyKnockback (which in turn may trigger ragdoll if the resulting
	/// peak knockback speed reaches Player.RagdollImpactThreshold).
	///
	/// One script, two prefab variants:
	///   - TrainingDummy      — light shove (sub-ragdoll-threshold)
	///   - MegaTrainingDummy  — hard hit (above threshold, triggers ragdoll)
	/// All gameplay state lives on the state authority and replicates via [Networked]
	/// fields and OnChangedRender hooks; clients just render what they see.
	/// </summary>
	public class TrainingDummy : NetworkBehaviour
	{
		[Header("References")]
		public Health Health;

		[Header("Combat")]
		[Tooltip("If false the dummy is a pure damage sponge: no scanning, no facing, no knockback, no damage.")]
		public bool Attacks = true;
		[Tooltip("Players within this radius (meters) will be punched.")]
		public float PunchRange = 2.5f;
		[Tooltip("Seconds between consecutive punches.")]
		public float PunchCooldown = 1.5f;
		[Tooltip("Damage dealt to the player per punch. Set to 0 for harmless training dummies.")]
		public int PunchDamage = 1;
		[Tooltip("Distance (meters) the player is pushed by a punch. Peak speed and duration are derived on the player from this and Player.KnockbackDeceleration; if the resulting peak speed exceeds Player.RagdollImpactThreshold the player goes ragdoll.")]
		public float KnockbackDistance = 1.5f;

		[Header("Detection")]
		[Tooltip("Layer mask used to find players. Default targets the 'Player' layer (6) used by this project's player prefab.")]
		public LayerMask PlayerLayerMask = 1 << 6;

		[Networked]
		private TickTimer _punchCooldownTimer { get; set; }
		[Networked, OnChangedRender(nameof(OnPunchCountChanged))]
		private int _punchCount { get; set; }

		private int _visiblePunchCount;
		private static readonly Collider[] _overlapBuffer = new Collider[16];

		public override void Spawned()
		{
			_visiblePunchCount = _punchCount;
		}

		public override void FixedUpdateNetwork()
		{
			// All AI runs on the state authority; clients just see the replicated
			// _punchCount counter and the player-side ragdoll state.
			if (HasStateAuthority == false) return;
			if (Health == null) return;

			if (Health.IsAlive == false)
			{
				// Auto-revive once the death cooldown elapses so the dummy is reusable for training.
				// Health.CurrentHealth is [Networked], so the revive replicates to every peer.
				if (Health.IsFinished)
				{
					Health.Revive();
				}
				return;
			}

			if (Attacks == false) return;

			if (_punchCooldownTimer.ExpiredOrNotRunning(Runner) == false) return;

			Player target = FindClosestPlayerInRange();
			if (target == null) return;

			// Face the target (cosmetic; replicated via NetworkTransform if attached, otherwise local).
			Vector3 toTarget = target.transform.position - transform.position;
			toTarget.y = 0f;
			if (toTarget.sqrMagnitude > 0.0001f)
			{
				transform.rotation = Quaternion.LookRotation(toTarget.normalized);
			}

			if (PunchDamage > 0 && target.Health != null)
			{
				target.Health.TakeHit(PunchDamage);
			}

			// ApplyKnockback is state-auth-only on Player; we're running on the state
			// authority of the dummy, which (in AutoHostOrClient) is also state auth
			// of the players. The push and any resulting ragdoll replicates from there.
			target.ApplyKnockback(transform.position, KnockbackDistance);

			_punchCooldownTimer = TickTimer.CreateFromSeconds(Runner, PunchCooldown);
			_punchCount++;
		}

		private Player FindClosestPlayerInRange()
		{
			int count = Physics.OverlapSphereNonAlloc(
				transform.position, PunchRange, _overlapBuffer, PlayerLayerMask, QueryTriggerInteraction.Ignore);

			Player closest = null;
			float closestSqr = float.MaxValue;
			for (int i = 0; i < count; i++)
			{
				var player = _overlapBuffer[i].GetComponentInParent<Player>();
				if (player == null) continue;
				if (player.Health == null || player.Health.IsAlive == false) continue;

				float sqr = (player.transform.position - transform.position).sqrMagnitude;
				if (sqr < closestSqr)
				{
					closest = player;
					closestSqr = sqr;
				}
			}
			return closest;
		}

		private void OnPunchCountChanged()
		{
			// Hook for SFX/animation on every peer when a punch lands.
			// Counter pattern (not RPC) tolerates dropped ticks better.
			_visiblePunchCount = _punchCount;
		}

		private void OnDrawGizmosSelected()
		{
			Gizmos.color = Color.red;
			Gizmos.DrawWireSphere(transform.position, PunchRange);
		}
	}
}
