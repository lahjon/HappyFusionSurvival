using Fusion;
using UnityEngine;
using UnityEngine.AI;

namespace Starter.Shooter
{
	/// <summary>
	/// Networked NPC. All tuning lives directly on this component — no external
	/// NpcDefinition required. NpcSpawner still passes one to Initialize() for
	/// phase/prefab management, but NpcAgent copies the values out and drops the ref.
	///
	/// All AI runs on the state authority; NavMeshAgent is disabled on proxies and
	/// motion replicates through the inherited NetworkTransform.
	/// </summary>
	[RequireComponent(typeof(NavMeshAgent))]
	public sealed class NpcAgent : NPC
	{
		private enum RuntimeState : byte { Passive, Chase, ReturnHome }

		// ── Behavior ──────────────────────────────────────────────────────────
		[Header("Behavior")]
		[Tooltip("Passive routine: stand still, roam randomly, or follow waypoints.")]
		public NpcBehavior Behavior = NpcBehavior.Wander;

		// ── Movement ──────────────────────────────────────────────────────────
		[Header("Movement")]
		[Tooltip("NavMeshAgent speed during passive routine and return-home.")]
		[Min(0f)] public float MoveSpeed = 1.5f;
		[Tooltip("Distance from destination that counts as arrived (meters).")]
		[Min(0.01f)] public float ArrivalTolerance = 0.3f;

		// ── Idle ──────────────────────────────────────────────────────────────
		[Header("Idle")]
		[Tooltip("Slowly rotate in place while idling instead of standing still.")]
		public bool LookAround = false;
		[Tooltip("Degrees per second when LookAround is on.")]
		[Min(0f)] public float LookAroundSpeed = 30f;

		// ── Wander ────────────────────────────────────────────────────────────
		[Header("Wander")]
		[Tooltip("Roam radius around spawn when no NpcMovementArea is assigned.")]
		[Min(0f)] public float WanderRadius = 4f;
		[Tooltip("Minimum seconds paused between wander destinations.")]
		[Min(0f)] public float WanderIdleMin = 1f;
		[Tooltip("Maximum seconds paused between wander destinations.")]
		[Min(0f)] public float WanderIdleMax = 3f;

		// ── Patrol ────────────────────────────────────────────────────────────
		[Header("Patrol")]
		public PatrolMode PatrolMode = PatrolMode.Loop;
		[Tooltip("Seconds paused at each waypoint.")]
		[Min(0f)] public float WaypointWaitSeconds = 1f;

		// ── Hostile ───────────────────────────────────────────────────────────
		[Header("Hostile (optional)")]
		[Tooltip("When true, aggros and chases the nearest living player in range.")]
		public bool Hostile = false;
		[Tooltip("Attack used while a target is within range.")]
		public CombatAction Attack;
		[Tooltip("Detection radius for players.")]
		[Min(0f)] public float AggroRadius = 10f;
		[Tooltip("NavMeshAgent speed while chasing.")]
		[Min(0f)] public float ChaseSpeed = 4.5f;
		[Tooltip("Seconds target must stay out of range before the NPC gives up.")]
		[Min(0f)] public float ResetGraceSeconds = 3f;
		[Tooltip("Ticks between chase repaths. Lower = more responsive.")]
		[Min(1)] public int ChaseRepathTicks = 5;
		[Tooltip("Abandon chase when target leaves the movement area.")]
		public bool LeashChaseToArea = true;

		// ── Scene refs ────────────────────────────────────────────────────────
		[Header("Scene references (authority-only; usually set by NpcSpawner)")]
		[Tooltip("Box the NPC roams within. Required for Wander; also leashes chase.")]
		public NpcMovementArea Area;
		[Tooltip("Patrol waypoints. Required for Patrol behavior.")]
		public NpcPatrolPath Path;

		/// <summary>
		/// Player this NPC is currently attending. While set the NPC stops and faces
		/// that player. Authority-written via RPC; replicates to all peers.
		/// </summary>
		[Networked] public PlayerRef AttendedPlayer { get; set; }

		private NavMeshAgent _agent;
		private ActionInvoker _invoker;
		private Animator _animator;
		private Vector3 _home;
		private RuntimeState _state;

		private Vector3 _prevRenderPos;
		private static readonly int _hashSpeed       = Animator.StringToHash("Speed");
		private static readonly int _hashMotionSpeed = Animator.StringToHash("MotionSpeed");
		private static readonly int _hashGrounded    = Animator.StringToHash("Grounded");
		private static readonly int _hashFreeFall    = Animator.StringToHash("FreeFall");

		// Per-instance RNG — seeded from network id so each NPC wanders independently.
		private System.Random _rng;

		// Passive bookkeeping (authority-local).
		private TickTimer _idleTimer;
		private int _patrolIndex;
		private int _patrolDir = 1;
		private bool _waitingAtWaypoint;

		// Hostile bookkeeping.
		private TickTimer _outOfRangeTimer;
		private int _lastChaseRepathTick;

		// Interaction bookkeeping (authority-local).
		private const float FaceLerpDuration = 0.3f;
		private bool _wasAttending;
		private Quaternion _faceStartRot;
		private TickTimer _faceTimer;
		private TickTimer _resumeTimer;

		private static readonly Collider[] _overlapBuffer = new Collider[16];

		private void Awake()
		{
			_agent    = GetComponent<NavMeshAgent>();
			_invoker  = GetComponent<ActionInvoker>();
			_animator = GetComponent<Animator>();
		}

		/// <summary>
		/// Called by NpcSpawner before Spawned(). Copies tuning values out of the
		/// definition so the spawner workflow still works without NpcAgent holding a ref.
		/// </summary>
		public void Initialize(NpcDefinition definition, NpcMovementArea area, NpcPatrolPath path)
		{
			if (definition != null)
			{
				Behavior           = definition.Behavior;
				MoveSpeed          = definition.MoveSpeed;
				ArrivalTolerance   = definition.ArrivalTolerance;
				LookAround         = definition.LookAround;
				LookAroundSpeed    = definition.LookAroundSpeed;
				WanderRadius       = definition.WanderRadius;
				WanderIdleMin      = definition.WanderIdleMin;
				WanderIdleMax      = definition.WanderIdleMax;
				PatrolMode         = definition.PatrolMode;
				WaypointWaitSeconds= definition.WaypointWaitSeconds;
				Hostile            = definition.Hostile;
				Attack             = definition.Attack;
				AggroRadius        = definition.AggroRadius;
				ChaseSpeed         = definition.ChaseSpeed;
				ResetGraceSeconds  = definition.ResetGraceSeconds;
				ChaseRepathTicks   = definition.ChaseRepathTicks;
				LeashChaseToArea   = definition.LeashChaseToArea;
			}
			Area = area;
			Path = path;
		}

		public override void Spawned()
		{
			_home          = transform.position;
			_prevRenderPos = transform.position;

			if (HasStateAuthority == false)
			{
				_agent.enabled = false;
				return;
			}

			_rng = new System.Random(unchecked((int)(Object.Id.Raw * 2654435761u + 1013904223u)));

			_agent.speed            = MoveSpeed;
			_agent.stoppingDistance = ArrivalTolerance;

			_state             = RuntimeState.Passive;
			_waitingAtWaypoint = false;
			_idleTimer         = TickTimer.CreateFromSeconds(Runner, 0f);
			if (Behavior == NpcBehavior.Patrol && Path != null)
				_patrolIndex = Path.ClosestIndex(transform.position);
		}

		protected override void OnFixedUpdateAlive()
		{
			if (HasStateAuthority == false) return;

			if (AttendedPlayer != PlayerRef.None)
			{
				var customer = Runner.GetPlayerObject(AttendedPlayer);
				if (customer != null)
				{
					if (_wasAttending == false)
					{
						_wasAttending = true;
						_faceStartRot = transform.rotation;
						_faceTimer    = TickTimer.CreateFromSeconds(Runner, FaceLerpDuration);
					}
					if (_agent.enabled && _agent.isOnNavMesh && _agent.hasPath) _agent.ResetPath();
					FaceCustomerLerped(customer.transform.position);
					return;
				}
				AttendedPlayer = PlayerRef.None;
			}

			if (_wasAttending)
			{
				_wasAttending = false;
				_resumeTimer  = TickTimer.CreateFromSeconds(Runner, RandRange(2f, 5f));
			}
			if (_resumeTimer.ExpiredOrNotRunning(Runner) == false)
			{
				if (_agent.enabled && _agent.isOnNavMesh && _agent.hasPath) _agent.ResetPath();
				return;
			}

			if (_agent.enabled == false || _agent.isOnNavMesh == false) return;

			Player target = (Hostile && Attack != null) ? FindClosestPlayerInAggro() : null;

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

		// ─── Passive ──────────────────────────────────────────────────────────

		private void TickPassive(Player target)
		{
			if (target != null) { EnterChase(); return; }

			switch (Behavior)
			{
				case NpcBehavior.Idle:   TickIdle();   break;
				case NpcBehavior.Wander: TickWander(); break;
				case NpcBehavior.Patrol: TickPatrol(); break;
			}
		}

		private void TickIdle()
		{
			if (_agent.hasPath) _agent.ResetPath();
			if (LookAround)
				transform.Rotate(0f, LookAroundSpeed * Runner.DeltaTime, 0f);
		}

		private void TickWander()
		{
			if (_agent.hasPath)
			{
				if (HasArrived())
				{
					_agent.ResetPath();
					_idleTimer = TickTimer.CreateFromSeconds(Runner, RandRange(WanderIdleMin, WanderIdleMax));
				}
				return;
			}

			if (_idleTimer.ExpiredOrNotRunning(Runner))
			{
				if (TryPickWanderDestination(out Vector3 dest))
					_agent.SetDestination(dest);
				else
					_idleTimer = TickTimer.CreateFromSeconds(Runner, 0.5f);
			}
		}

		private bool TryPickWanderDestination(out Vector3 result)
		{
			if (Area != null) return Area.TryRandomPoint(_rng, out result);

			float r   = Mathf.Max(0.5f, WanderRadius);
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
					_patrolIndex       = Path.NextIndex(_patrolIndex, PatrolMode, ref _patrolDir);
					GoToCurrentWaypoint();
				}
				return;
			}

			if (_agent.hasPath == false)
				GoToCurrentWaypoint();
			else if (HasArrived())
			{
				_agent.ResetPath();
				_waitingAtWaypoint = true;
				_idleTimer         = TickTimer.CreateFromSeconds(Runner, WaypointWaitSeconds);
			}
		}

		private void GoToCurrentWaypoint()
		{
			if (Path.TryGetPoint(_patrolIndex, out Vector3 pos)) _agent.SetDestination(pos);
		}

		// ─── Hostile ──────────────────────────────────────────────────────────

		private void TickChase(Player target)
		{
			bool lost = target == null
				|| (LeashChaseToArea && Area != null && Area.Contains(target.transform.position) == false);

			if (lost)
			{
				if (_outOfRangeTimer.IsRunning == false)
					_outOfRangeTimer = TickTimer.CreateFromSeconds(Runner, ResetGraceSeconds);
				else if (_outOfRangeTimer.Expired(Runner))
					EnterReturnHome();
				return;
			}

			_outOfRangeTimer = default;
			FaceTarget(target.transform.position);

			if ((int)Runner.Tick - _lastChaseRepathTick >= ChaseRepathTicks)
			{
				_agent.SetDestination(target.transform.position);
				_lastChaseRepathTick = (int)Runner.Tick;
			}

			float range   = Attack != null ? Attack.EffectiveRange : 0f;
			float distSqr = (target.transform.position - transform.position).sqrMagnitude;
			if (range > 0f && distSqr <= range * range) TryAttack();
		}

		private void TickReturnHome(Player target)
		{
			if (target != null) { EnterChase(); return; }
			if (HasArrived()) EnterPassive();
		}

		// ─── Transitions ──────────────────────────────────────────────────────

		private void EnterPassive()
		{
			_state             = RuntimeState.Passive;
			_agent.speed       = MoveSpeed;
			if (_agent.isOnNavMesh) _agent.ResetPath();
			_waitingAtWaypoint = false;
			_idleTimer         = TickTimer.CreateFromSeconds(Runner, 0f);
			if (Behavior == NpcBehavior.Patrol && Path != null)
				_patrolIndex = Path.ClosestIndex(transform.position);
		}

		private void EnterChase()
		{
			_state               = RuntimeState.Chase;
			_agent.speed         = ChaseSpeed;
			_outOfRangeTimer     = default;
			_lastChaseRepathTick = (int)Runner.Tick - ChaseRepathTicks;
		}

		private void EnterReturnHome()
		{
			_state           = RuntimeState.ReturnHome;
			_agent.speed     = MoveSpeed;
			_outOfRangeTimer = default;
			if (_agent.isOnNavMesh) _agent.SetDestination(_home);
		}

		// ─── Helpers ──────────────────────────────────────────────────────────

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

		private void FaceCustomerLerped(Vector3 targetPos)
		{
			Vector3 toTarget = targetPos - transform.position;
			toTarget.y = 0f;
			if (toTarget.sqrMagnitude < 0.0001f) return;

			Quaternion targetRot = Quaternion.LookRotation(toTarget.normalized);
			float remaining      = _faceTimer.RemainingTime(Runner) ?? 0f;
			float t              = Mathf.Clamp01(1f - remaining / FaceLerpDuration);
			transform.rotation   = Quaternion.Slerp(_faceStartRot, targetRot, t);
		}

		private Player FindClosestPlayerInAggro()
		{
			int count = Physics.OverlapSphereNonAlloc(
				transform.position, AggroRadius, _overlapBuffer, CombatAction.HitMask, QueryTriggerInteraction.Ignore);

			Player closest    = null;
			float closestSqr  = float.MaxValue;
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
			if (Attack == null || _invoker == null) return;
			if (_invoker.CanFire == false) return;

			var ctx = new ActorContext
			{
				Runner            = Runner,
				IgnoreAuthority   = default,
				AttackerPosition  = transform.position,
				FireTransform     = transform,
				AttackerRoot      = gameObject,
				IsStateAuthority  = HasStateAuthority,
			};

			_invoker.TryFire(Attack, in ctx, false);
		}

		// ─── Interaction ──────────────────────────────────────────────────────

		public void LocalBeginAttending()
		{
			if (Object != null) RPC_SetAttended(true);
		}

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

		// ─── Render ───────────────────────────────────────────────────────────

		public override void Render()
		{
			if (_animator == null) return;

			float dt    = Time.deltaTime;
			float speed = 0f;
			if (dt > 0f)
			{
				Vector3 delta = transform.position - _prevRenderPos;
				delta.y       = 0f;
				speed         = delta.magnitude / dt;
			}
			_prevRenderPos = transform.position;

			_animator.SetFloat(_hashSpeed,       speed);
			_animator.SetFloat(_hashMotionSpeed, 1f);
			_animator.SetBool (_hashGrounded,    true);
			_animator.SetBool (_hashFreeFall,    false);
		}

		// ─── Gizmos ───────────────────────────────────────────────────────────

		private void OnDrawGizmosSelected()
		{
			if (!Hostile) return;

			Gizmos.color = Color.red;
			Gizmos.DrawWireSphere(transform.position, AggroRadius);
			if (Attack != null && Attack.EffectiveRange > 0f)
			{
				Gizmos.color = Color.magenta;
				Gizmos.DrawWireSphere(transform.position, Attack.EffectiveRange);
			}
		}

		public void TriggerAnimation(string triggerName)
		{
			_animator.SetTrigger(triggerName);
		}
	}
}
