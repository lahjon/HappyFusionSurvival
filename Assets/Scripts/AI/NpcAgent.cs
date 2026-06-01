using Fusion;
using UnityEngine;
using UnityEngine.AI;

namespace Starter.Shooter
{
	/// <summary>
	/// Data-driven networked NPC. Reads an <see cref="NpcDefinition"/> for its passive
	/// routine (Idle / Wander / Patrol) plus an optional Hostile chase+attack layer, and
	/// stays inside an <see cref="NpcMovementArea"/>. Generalizes <see cref="EvilChicken"/>.
	///
	/// All AI runs on the state authority; <see cref="NavMeshAgent"/> is disabled on
	/// proxies and motion replicates through the inherited NetworkTransform. Runtime
	/// bookkeeping (state, patrol index, timers) is authority-local — proxies never
	/// simulate, so none of it is [Networked].
	/// </summary>
	[RequireComponent(typeof(NavMeshAgent))]
	public sealed class NpcAgent : NPC
	{
		private enum RuntimeState : byte { Passive, Chase, ReturnHome }

		[Header("Config")]
		[Tooltip("NPC type config. Set in the prefab, or injected by NpcSpawner at spawn time.")]
		public NpcDefinition Definition;

		[Header("Scene references (authority-only; usually set by NpcSpawner)")]
		[Tooltip("Box the NPC is allowed to roam within. Required for Wander; also leashes the chase.")]
		public NpcMovementArea Area;
		[Tooltip("Patrol waypoints. Required for Patrol behavior.")]
		public NpcPatrolPath Path;

		/// <summary>
		/// Player this NPC is currently attending (talking to / serving). While set, the NPC
		/// stops moving and faces that player. Authority-written via <see cref="RPC_SetAttended"/>;
		/// replicates so every peer sees the NPC turn to its customer.
		/// </summary>
		[Networked] public PlayerRef AttendedPlayer { get; set; }

		private NavMeshAgent _agent;
		private ActionInvoker _invoker;
		private Vector3 _home;
		private RuntimeState _state;

		// Per-instance RNG so each NPC wanders independently (deterministic, seeded from the
		// network id). Shared UnityEngine.Random made identically-configured NPCs move in lockstep.
		private System.Random _rng;

		// Passive bookkeeping (authority-local).
		private TickTimer _idleTimer;          // wander idle / patrol wait
		private int _patrolIndex;
		private int _patrolDir = 1;
		private bool _waitingAtWaypoint;

		// Hostile bookkeeping.
		private TickTimer _outOfRangeTimer;
		private int _lastChaseRepathTick;

		// Interaction bookkeeping (authority-local).
		private const float FaceLerpDuration = 0.3f;   // time to smoothly turn toward a new customer
		private bool _wasAttending;
		private Quaternion _faceStartRot;
		private TickTimer _faceTimer;
		private TickTimer _resumeTimer;                // post-interaction pause before resuming behavior

		private static readonly Collider[] _overlapBuffer = new Collider[16];

		private void Awake()
		{
			_agent = GetComponent<NavMeshAgent>();
			_invoker = GetComponent<ActionInvoker>();
		}

		/// <summary>
		/// Injects config + scene refs. Called by <see cref="NpcSpawner"/> on the authority
		/// inside Runner.Spawn's onBeforeSpawned callback, so the fields are set before
		/// <see cref="Spawned"/> runs.
		/// </summary>
		public void Initialize(NpcDefinition definition, NpcMovementArea area, NpcPatrolPath path)
		{
			Definition = definition;
			Area = area;
			Path = path;
		}

		public override void Spawned()
		{
			_home = transform.position;

			if (HasStateAuthority == false)
			{
				// Proxies must not drive movement — NetworkTransform replicates it.
				_agent.enabled = false;
				return;
			}

			// Seed per-instance so two identical NPCs don't pick the same wander points / idle times.
			_rng = new System.Random(unchecked((int)(Object.Id.Raw * 2654435761u + 1013904223u)));

			ApplyDefinition();

			_state = RuntimeState.Passive;
			_waitingAtWaypoint = false;
			_idleTimer = TickTimer.CreateFromSeconds(Runner, 0f);
			if (Definition != null && Definition.Behavior == NpcBehavior.Patrol && Path != null)
				_patrolIndex = Path.ClosestIndex(transform.position);
		}

		private void ApplyDefinition()
		{
			if (Definition == null || _agent == null) return;
			_agent.speed = Definition.MoveSpeed;
			_agent.stoppingDistance = Definition.ArrivalTolerance;
		}

		protected override void OnFixedUpdateAlive()
		{
			if (HasStateAuthority == false) return;
			if (Definition == null) return;

			// Attending a customer takes priority over everything: stop and smoothly face them.
			if (AttendedPlayer != PlayerRef.None)
			{
				var customer = Runner.GetPlayerObject(AttendedPlayer);
				if (customer != null)
				{
					if (_wasAttending == false)
					{
						// Just started — capture the turn-from rotation for the 0.3s lerp.
						_wasAttending = true;
						_faceStartRot = transform.rotation;
						_faceTimer = TickTimer.CreateFromSeconds(Runner, FaceLerpDuration);
					}
					if (_agent.enabled && _agent.isOnNavMesh && _agent.hasPath) _agent.ResetPath();
					FaceCustomerLerped(customer.transform.position);
					return;
				}
				// Customer disconnected — drop attention and resume.
				AttendedPlayer = PlayerRef.None;
			}

			// Interaction just ended — pause 2–5s before wandering off again.
			if (_wasAttending)
			{
				_wasAttending = false;
				_resumeTimer = TickTimer.CreateFromSeconds(Runner, RandRange(2f, 5f));
			}
			if (_resumeTimer.ExpiredOrNotRunning(Runner) == false)
			{
				if (_agent.enabled && _agent.isOnNavMesh && _agent.hasPath) _agent.ResetPath();
				return;
			}

			if (_agent.enabled == false || _agent.isOnNavMesh == false) return;

			Player target = (Definition.Hostile && Definition.Attack != null) ? FindClosestPlayerInAggro() : null;

			switch (_state)
			{
				case RuntimeState.Passive:    TickPassive(target);    break;
				case RuntimeState.Chase:      TickChase(target);      break;
				case RuntimeState.ReturnHome: TickReturnHome(target); break;
			}
		}

		protected override void OnFixedUpdateDead()
		{
			if (HasStateAuthority == false) return;
			if (_agent.enabled && _agent.isOnNavMesh) _agent.ResetPath();
		}

		// ─── Passive: Idle / Wander / Patrol ─────────────────────────────────

		private void TickPassive(Player target)
		{
			if (target != null) { EnterChase(); return; }

			switch (Definition.Behavior)
			{
				case NpcBehavior.Idle:   TickIdle();   break;
				case NpcBehavior.Wander: TickWander(); break;
				case NpcBehavior.Patrol: TickPatrol(); break;
			}
		}

		private void TickIdle()
		{
			if (_agent.hasPath) _agent.ResetPath();
			if (Definition.LookAround)
				transform.Rotate(0f, Definition.LookAroundSpeed * Runner.DeltaTime, 0f);
		}

		private void TickWander()
		{
			if (_agent.hasPath)
			{
				// Walking toward the current destination — when we get there, pause for a bit.
				if (HasArrived())
				{
					_agent.ResetPath();
					_idleTimer = TickTimer.CreateFromSeconds(
						Runner, RandRange(Definition.WanderIdleMin, Definition.WanderIdleMax));
				}
				return;
			}

			// Idle (no path): once the pause elapses, pick the next destination and move on.
			// Guard the pick behind the timer so we don't re-arm it every tick (which would
			// freeze the NPC forever — HasArrived() is trivially true while pathless).
			if (_idleTimer.ExpiredOrNotRunning(Runner))
			{
				if (TryPickWanderDestination(out Vector3 dest))
					_agent.SetDestination(dest);
				else
					_idleTimer = TickTimer.CreateFromSeconds(Runner, 0.5f); // no navmesh sample — retry soon
			}
		}

		/// <summary>
		/// Picks a wander destination: inside the <see cref="Area"/> if one is assigned, otherwise
		/// within <see cref="NpcDefinition.WanderRadius"/> of the spawn point. Uses the per-instance
		/// RNG so each NPC roams independently.
		/// </summary>
		private bool TryPickWanderDestination(out Vector3 result)
		{
			if (Area != null) return Area.TryRandomPoint(_rng, out result);

			float r = Mathf.Max(0.5f, Definition.WanderRadius);
			float ang = (float)_rng.NextDouble() * Mathf.PI * 2f;
			float rad = Mathf.Sqrt((float)_rng.NextDouble()) * r;
			Vector3 candidate = _home + new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
			if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, r, NavMesh.AllAreas))
			{
				result = hit.position;
				return true;
			}
			result = default;
			return false;
		}

		private float RandRange(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

		private void TickPatrol()
		{
			if (Path == null || Path.Count == 0) { TickIdle(); return; }

			if (_waitingAtWaypoint)
			{
				if (_idleTimer.ExpiredOrNotRunning(Runner))
				{
					_waitingAtWaypoint = false;
					_patrolIndex = Path.NextIndex(_patrolIndex, Definition.PatrolMode, ref _patrolDir);
					GoToCurrentWaypoint();
				}
				return;
			}

			if (_agent.hasPath == false)
			{
				GoToCurrentWaypoint();
			}
			else if (HasArrived())
			{
				_agent.ResetPath();
				_waitingAtWaypoint = true;
				_idleTimer = TickTimer.CreateFromSeconds(Runner, Definition.WaypointWaitSeconds);
			}
		}

		private void GoToCurrentWaypoint()
		{
			if (Path.TryGetPoint(_patrolIndex, out Vector3 pos)) _agent.SetDestination(pos);
		}

		// ─── Hostile: Chase / ReturnHome ─────────────────────────────────────

		private void TickChase(Player target)
		{
			bool lost = target == null
				|| (Definition.LeashChaseToArea && Area != null && Area.Contains(target.transform.position) == false);

			if (lost)
			{
				if (_outOfRangeTimer.IsRunning == false)
					_outOfRangeTimer = TickTimer.CreateFromSeconds(Runner, Definition.ResetGraceSeconds);
				else if (_outOfRangeTimer.Expired(Runner))
					EnterReturnHome();
				return;
			}

			// Target in range — cancel the give-up countdown.
			_outOfRangeTimer = default;
			FaceTarget(target.transform.position);

			// Repath periodically; the agent's stoppingDistance halts it at the target.
			if ((int)Runner.Tick - _lastChaseRepathTick >= Definition.ChaseRepathTicks)
			{
				_agent.SetDestination(target.transform.position);
				_lastChaseRepathTick = (int)Runner.Tick;
			}

			float range = Definition.Attack != null ? Definition.Attack.EffectiveRange : 0f;
			float distSqr = (target.transform.position - transform.position).sqrMagnitude;
			if (range > 0f && distSqr <= range * range) TryAttack();
		}

		private void TickReturnHome(Player target)
		{
			if (target != null) { EnterChase(); return; }
			if (HasArrived()) EnterPassive();
		}

		// ─── Transitions ─────────────────────────────────────────────────────

		private void EnterPassive()
		{
			_state = RuntimeState.Passive;
			_agent.speed = Definition.MoveSpeed;
			if (_agent.isOnNavMesh) _agent.ResetPath();
			_waitingAtWaypoint = false;
			_idleTimer = TickTimer.CreateFromSeconds(Runner, 0f);
			if (Definition.Behavior == NpcBehavior.Patrol && Path != null)
				_patrolIndex = Path.ClosestIndex(transform.position);
		}

		private void EnterChase()
		{
			_state = RuntimeState.Chase;
			_agent.speed = Definition.ChaseSpeed;
			_outOfRangeTimer = default;
			// Force an immediate repath. Subtracting ChaseRepathTicks (instead of int.MinValue)
			// avoids overflowing the (int)Runner.Tick - _lastChaseRepathTick comparison.
			_lastChaseRepathTick = (int)Runner.Tick - Definition.ChaseRepathTicks;
		}

		private void EnterReturnHome()
		{
			_state = RuntimeState.ReturnHome;
			_agent.speed = Definition.MoveSpeed;
			_outOfRangeTimer = default;
			if (_agent.isOnNavMesh) _agent.SetDestination(_home);
		}

		// ─── Helpers (shared shape with EvilChicken) ─────────────────────────

		private bool HasArrived()
		{
			if (_agent.pathPending) return false;
			return _agent.remainingDistance <= Mathf.Max(Definition.ArrivalTolerance, _agent.stoppingDistance);
		}

		private void FaceTarget(Vector3 targetPos)
		{
			Vector3 toTarget = targetPos - transform.position;
			toTarget.y = 0f;
			if (toTarget.sqrMagnitude < 0.0001f) return;
			transform.rotation = Quaternion.LookRotation(toTarget.normalized);
		}

		/// <summary>
		/// Turns to face the customer over <see cref="FaceLerpDuration"/> (lerp from the rotation
		/// captured when attention started), then tracks them directly once the lerp completes.
		/// </summary>
		private void FaceCustomerLerped(Vector3 targetPos)
		{
			Vector3 toTarget = targetPos - transform.position;
			toTarget.y = 0f;
			if (toTarget.sqrMagnitude < 0.0001f) return;

			Quaternion targetRot = Quaternion.LookRotation(toTarget.normalized);
			float remaining = _faceTimer.RemainingTime(Runner) ?? 0f;
			float t = Mathf.Clamp01(1f - remaining / FaceLerpDuration);
			transform.rotation = Quaternion.Slerp(_faceStartRot, targetRot, t);
		}

		private Player FindClosestPlayerInAggro()
		{
			int count = Physics.OverlapSphereNonAlloc(
				transform.position, Definition.AggroRadius, _overlapBuffer, CombatAction.HitMask, QueryTriggerInteraction.Ignore);

			Player closest = null;
			float closestSqr = float.MaxValue;
			for (int i = 0; i < count; i++)
			{
				var player = _overlapBuffer[i].GetComponentInParent<Player>();
				if (player == null) continue;
				if (player.Health == null || player.Health.IsAlive == false) continue;

				float sqr = (player.transform.position - transform.position).sqrMagnitude;
				if (sqr < closestSqr) { closest = player; closestSqr = sqr; }
			}
			return closest;
		}

		private void TryAttack()
		{
			if (Definition.Attack == null || _invoker == null) return;
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

			_invoker.TryFire(Definition.Attack, in ctx, false);
		}

		// ─── Interaction ─────────────────────────────────────────────────────
		// NpcAgent intentionally does NOT implement IInteractable — interaction is a
		// separate component on the same GameObject (Shopkeeper, QuestGiver, or a future
		// NpcDialogue : InteractableStation). The interaction component calls the Local*
		// helpers below when a player opens/closes its session, so movement pauses and the
		// NPC turns to face the customer (see AttendedPlayer + OnFixedUpdateAlive).

		/// <summary>Local player started interacting — ask the authority to make this NPC attend us.</summary>
		public void LocalBeginAttending()
		{
			if (Object != null) RPC_SetAttended(true);
		}

		/// <summary>Local player stopped interacting — release attention if we were the customer.</summary>
		public void LocalEndAttending()
		{
			if (Object != null) RPC_SetAttended(false);
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_SetAttended(NetworkBool attending, RpcInfo info = default)
		{
			var source = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
			if (attending)
				AttendedPlayer = source;
			else if (AttendedPlayer == source)
				AttendedPlayer = PlayerRef.None;
		}

		private void OnDrawGizmosSelected()
		{
			if (Definition == null || Definition.Hostile == false) return;

			Gizmos.color = Color.red;
			Gizmos.DrawWireSphere(transform.position, Definition.AggroRadius);
			if (Definition.Attack != null && Definition.Attack.EffectiveRange > 0f)
			{
				Gizmos.color = Color.magenta;
				Gizmos.DrawWireSphere(transform.position, Definition.Attack.EffectiveRange);
			}
		}
	}
}
