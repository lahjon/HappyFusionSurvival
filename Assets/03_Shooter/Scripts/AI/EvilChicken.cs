using Fusion;
using UnityEngine;
using UnityEngine.AI;

namespace Starter.Shooter
{
	/// <summary>
	/// Hostile NPC: wanders inside a home area, chases the nearest player that
	/// enters its aggro radius, attacks via <see cref="ActionInvoker"/>, and
	/// returns home if the target stays outside the aggro radius for
	/// <see cref="ResetGraceSeconds"/>.
	///
	/// All AI runs on the state authority; NavMeshAgent is disabled on proxies
	/// and motion is replicated through NetworkTransform.
	/// </summary>
	[RequireComponent(typeof(ActionInvoker), typeof(NavMeshAgent))]
	public sealed class EvilChicken : NPC
	{
		private enum State : byte { Wander, Chase, Reset }

		[Header("Combat")]
		[Tooltip("Attack used while a target is within EffectiveRange. Also defines the player layer mask used by aggro detection.")]
		public CombatAction Attack;

		[Header("Aggro")]
		[Tooltip("Radius around the chicken in which players are detected and chased.")]
		public float AggroRadius = 10f;
		[Tooltip("Seconds the target must stay outside AggroRadius before the chicken gives up and walks home.")]
		public float ResetGraceSeconds = 3f;

		[Header("Wander")]
		[Tooltip("How far from HomeCenter the chicken will pick wander destinations.")]
		public float WanderRadius = 6f;
		[Tooltip("Minimum seconds spent idling between wander destinations.")]
		public float WanderIdleMin = 1f;
		[Tooltip("Maximum seconds spent idling between wander destinations.")]
		public float WanderIdleMax = 3f;

		[Header("Movement")]
		public float WanderSpeed = 1.5f;
		public float ChaseSpeed = 4.5f;
		[Tooltip("How close to the current destination counts as arrived (meters).")]
		public float ArrivalTolerance = 0.3f;
		[Tooltip("How often (ticks) the chase destination is refreshed. Lower = more responsive, more cost.")]
		public int ChaseRepathTicks = 5;

		private NavMeshAgent _agent;
		private ActionInvoker _invoker;
		private Vector3 _homeCenter;
		private State _state;
		private TickTimer _wanderIdleTimer;
		private TickTimer _outOfAggroTimer;
		private int _lastChaseRepathTick;

		private static readonly Collider[] _overlapBuffer = new Collider[16];

		private void Awake()
		{
			_agent = GetComponent<NavMeshAgent>();
			_invoker = GetComponent<ActionInvoker>();
		}

		public override void Spawned()
		{
			_homeCenter = transform.position;

			if (HasStateAuthority == false)
			{
				// Proxies should not drive movement — NetworkTransform handles it.
				_agent.enabled = false;
				return;
			}

			_agent.speed = WanderSpeed;
			_state = State.Wander;
			_wanderIdleTimer = TickTimer.CreateFromSeconds(Runner, 0f);
		}

		protected override void OnFixedUpdateAlive()
		{
			if (HasStateAuthority == false) return;
			if (_agent.enabled == false || _agent.isOnNavMesh == false) return;

			Player target = FindClosestPlayerInAggro();

			switch (_state)
			{
				case State.Wander: TickWander(target); break;
				case State.Chase:  TickChase(target);  break;
				case State.Reset:  TickReset(target);  break;
			}
		}

		protected override void OnFixedUpdateDead()
		{
			if (HasStateAuthority == false) return;
			if (_agent.enabled && _agent.isOnNavMesh)
			{
				_agent.ResetPath();
			}
		}

		// ─── States ──────────────────────────────────────────────────────────

		private void TickWander(Player target)
		{
			if (target != null)
			{
				EnterChase();
				return;
			}

			if (_agent.hasPath == false && _wanderIdleTimer.ExpiredOrNotRunning(Runner))
			{
				if (TryPickWanderDestination(out Vector3 dest))
				{
					_agent.SetDestination(dest);
				}
				else
				{
					// No valid sample this tick — retry shortly.
					_wanderIdleTimer = TickTimer.CreateFromSeconds(Runner, 0.5f);
				}
			}
			else if (HasArrived())
			{
				_agent.ResetPath();
				_wanderIdleTimer = TickTimer.CreateFromSeconds(
					Runner, Random.Range(WanderIdleMin, WanderIdleMax));
			}
		}

		private void TickChase(Player target)
		{
			if (target == null)
			{
				if (_outOfAggroTimer.IsRunning == false)
				{
					_outOfAggroTimer = TickTimer.CreateFromSeconds(Runner, ResetGraceSeconds);
				}
				else if (_outOfAggroTimer.Expired(Runner))
				{
					EnterReset();
				}
				return;
			}

			// Target back in range — cancel reset countdown.
			_outOfAggroTimer = default;

			FaceTarget(target.transform.position);

			// Keep pathing every ChaseRepathTicks regardless of range — the agent's
			// stoppingDistance naturally halts it at the target. ResetPath'ing in
			// attack range prevents the chicken from following a player who keeps
			// moving (especially when their colliders overlap).
			if ((int)Runner.Tick - _lastChaseRepathTick >= ChaseRepathTicks)
			{
				_agent.SetDestination(target.transform.position);
				_lastChaseRepathTick = (int)Runner.Tick;
			}

			float range = Attack != null ? Attack.EffectiveRange : 0f;
			float distSqr = (target.transform.position - transform.position).sqrMagnitude;
			if (range > 0f && distSqr <= range * range)
			{
				TryAttack();
			}
		}

		private void TickReset(Player target)
		{
			if (target != null)
			{
				EnterChase();
				return;
			}

			if (HasArrived())
			{
				EnterWander();
			}
		}

		// ─── Transitions ─────────────────────────────────────────────────────

		private void EnterWander()
		{
			_state = State.Wander;
			_agent.speed = WanderSpeed;
			_agent.ResetPath();
			_wanderIdleTimer = TickTimer.CreateFromSeconds(
				Runner, Random.Range(WanderIdleMin, WanderIdleMax));
		}

		private void EnterChase()
		{
			_state = State.Chase;
			_agent.speed = ChaseSpeed;
			_outOfAggroTimer = default;
			// Force immediate repath. int.MinValue would overflow the
			// (int)Runner.Tick - _lastChaseRepathTick subtraction and skip every repath.
			_lastChaseRepathTick = (int)Runner.Tick - ChaseRepathTicks;
		}

		private void EnterReset()
		{
			_state = State.Reset;
			_agent.speed = WanderSpeed;
			_outOfAggroTimer = default;
			_agent.SetDestination(_homeCenter);
		}

		// ─── Helpers ─────────────────────────────────────────────────────────

		private bool TryPickWanderDestination(out Vector3 result)
		{
			Vector2 offset = Random.insideUnitCircle * WanderRadius;
			Vector3 candidate = _homeCenter + new Vector3(offset.x, 0f, offset.y);
			if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, WanderRadius, NavMesh.AllAreas))
			{
				result = hit.position;
				return true;
			}
			result = default;
			return false;
		}

		private bool HasArrived()
		{
			if (_agent.pathPending) return false;
			return _agent.remainingDistance <= Mathf.Max(ArrivalTolerance, _agent.stoppingDistance);
		}

		private void FaceTarget(Vector3 targetPos)
		{
			Vector3 toTarget = targetPos - transform.position;
			toTarget.y = 0f;
			if (toTarget.sqrMagnitude < 0.0001f) return;
			transform.rotation = Quaternion.LookRotation(toTarget.normalized);
		}

		private Player FindClosestPlayerInAggro()
		{
			if (Attack == null) return null;

			int count = Physics.OverlapSphereNonAlloc(
				transform.position, AggroRadius, _overlapBuffer, Attack.HitMask, QueryTriggerInteraction.Ignore);

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

		private void TryAttack()
		{
			if (Attack == null) return;
			if (_invoker.CanFire == false) return;

			var ctx = new ActorContext
			{
				Runner = Runner,
				IgnoreAuthority = default,
				AttackerPosition = transform.position,
				FireTransform = transform,
				AttackerRoot = gameObject,
				IsStateAuthority = HasStateAuthority,
			};

			_invoker.TryFire(Attack, in ctx, false);
		}

		private void OnDrawGizmosSelected()
		{
			Gizmos.color = Color.red;
			Gizmos.DrawWireSphere(transform.position, AggroRadius);
			Gizmos.color = Color.yellow;
			Vector3 home = Application.isPlaying ? _homeCenter : transform.position;
			Gizmos.DrawWireSphere(home, WanderRadius);
			if (Attack != null && Attack.EffectiveRange > 0f)
			{
				Gizmos.color = Color.magenta;
				Gizmos.DrawWireSphere(transform.position, Attack.EffectiveRange);
			}
		}
	}
}
