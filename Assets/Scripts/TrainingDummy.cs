using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Stationary practice enemy. Scans for players within the active CombatAction's
	/// effective range on the state authority's tick and, when the invoker is off
	/// cooldown, fires the action — which deals damage and routes knockback through
	/// <see cref="IKnockbackable.ApplyKnockback"/> (which in turn may trigger
	/// ragdoll if the resulting peak knockback speed reaches Player.RagdollImpactThreshold).
	///
	/// Variants are built by swapping the CombatAction asset in the prefab — same
	/// script, different SO. All gameplay state lives on the state authority and
	/// replicates via [Networked] fields (Cooldown on the invoker, _punchCount here).
	/// </summary>
	[RequireComponent(typeof(ActionInvoker))]
	public class TrainingDummy : NetworkBehaviour
	{
		[Header("References")]
		public Health Health;

		[Header("Combat")]
		[Tooltip("If false the dummy is a pure damage sponge: no scanning, no facing, no attacks.")]
		public bool Attacks = true;
		[Tooltip("Ordered list of CombatActions this dummy can perform. The first is used by default.")]
		[SerializeField] private List<CombatAction> _actions = new List<CombatAction>();

		public IReadOnlyList<CombatAction> Actions => _actions;

		private ActionInvoker _invoker;

		[Networked, OnChangedRender(nameof(OnPunchCountChanged))]
		private int _punchCount { get; set; }

		private int _visiblePunchCount;
		private static readonly Collider[] _overlapBuffer = new Collider[16];

		private void Awake()
		{
			_invoker = GetComponent<ActionInvoker>();
		}

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
			if (_actions == null || _actions.Count == 0) return;

			var action = _actions[0];
			if (action == null) return;
			if (_invoker.CanFire == false) return;

			Player target = FindClosestPlayerInRange(action);
			if (target == null) return;

			// Face the target (cosmetic; replicated via NetworkTransform if attached, otherwise local).
			Vector3 toTarget = target.transform.position - transform.position;
			toTarget.y = 0f;
			if (toTarget.sqrMagnitude > 0.0001f)
			{
				transform.rotation = Quaternion.LookRotation(toTarget.normalized);
			}

			var ctx = new ActorContext
			{
				Runner = Runner,
				IgnoreAuthority = default,
				AttackerPosition = transform.position,
				FireTransform = transform,
				AttackerRoot = gameObject,
				IsStateAuthority = HasStateAuthority,
			};

			var hit = _invoker.TryFire(action, in ctx, false);
			if (hit.DidFire)
			{
				_punchCount++;
			}
		}

		private Player FindClosestPlayerInRange(CombatAction action)
		{
			float range = action.EffectiveRange;
			if (range <= 0f) return null;

			int count = Physics.OverlapSphereNonAlloc(
				transform.position, range, _overlapBuffer, CombatAction.HitMask, QueryTriggerInteraction.Ignore);

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
			if (_actions == null || _actions.Count == 0 || _actions[0] == null) return;
			Gizmos.color = Color.red;
			Gizmos.DrawWireSphere(transform.position, _actions[0].EffectiveRange);
		}
	}
}
