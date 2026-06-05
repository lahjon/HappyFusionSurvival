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
		private enum RuntimeState : byte { Passive, Chase, ReturnHome, Retreat, Guard, GuardChase }

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

		// ── Personal space ────────────────────────────────────────────────────
		[Header("Personal space")]
		[Tooltip("When a player comes within this distance the NPC stops dead and stands idle until they step back out, instead of walking into / through them. 0 disables.")]
		[Min(0f)] public float PersonalSpaceRadius = 2f;

		// ── Pushable ──────────────────────────────────────────────────────────
		[Header("Pushable")]
		[Tooltip("While stopped for a nearby player, the NPC can still be physically shoved by players within this distance — so you can ease it out of a doorway. Stays in its idle pose while pushed. Should exceed NPC radius + player radius (~0.8m). 0 disables pushing.")]
		[Min(0f)] public float PushRadius = 1.2f;
		[Tooltip("How fast a player shoves the NPC, m/s at full contact. Gentle so you ease it aside, not launch it.")]
		[Min(0f)] public float PushStrength = 3.5f;

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

		// ── Building (optional) ───────────────────────────────────────────────
		[Header("Building (optional)")]
		[Tooltip("Home/territory this NPC retreats into at dusk and defends at night. Leave null for free-roaming NPCs.")]
		public NpcBuilding Building;
		[Tooltip("NavMeshAgent speed while hustling home during the dusk lockdown.")]
		[Min(0f)] public float RetreatSpeed = 3f;

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

		/// <summary>
		/// True while the NPC is holding still for a nearby player (stopped, but possibly being shoved
		/// by a push). Replicated so EVERY peer forces the idle animation even as the push slides the
		/// body — otherwise the position delta would read as "walking" on each client.
		/// </summary>
		[Networked] public NetworkBool Holding { get; set; }

		private NavMeshAgent _agent;
		private ActionInvoker _invoker;
		private Animator _animator;
		private Vector3 _home;
		private RuntimeState _state;

		// Close-range yielding (authority-local). Latched with hysteresis so the idle/walk state can't
		// flicker while a player hovers right around the personal-space boundary.
		private bool _crowdYielding;
		private const float CrowdHysteresis = 0.75f;

		private Vector3 _prevRenderPos;
		private const float SpeedDampSeconds = 0.12f;
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

		// Building retreat bookkeeping (authority-local).
		private NpcExit _retreatExit;

		// Stuck recovery (authority-local). Abandons a destination the agent can't make progress toward —
		// e.g. a path the navmesh bake routes through a window/narrow gap the body can't actually traverse —
		// instead of pinning against it forever. Mirrors BotBrain's wander give-up watchdog.
		private Vector3 _progressSamplePos;
		private TickTimer _progressTimer;
		private bool _wasPursuing;
		private const float ProgressSampleSeconds = 1.5f;
		private const float ProgressEpsilonSqr    = 0.5f * 0.5f; // < 0.5 m moved over a full window = pinned

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
				PersonalSpaceRadius= definition.PersonalSpaceRadius;
				PushRadius         = definition.PushRadius;
				PushStrength       = definition.PushStrength;
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

			// Join the household — the building drives retreat/hostility from the match phase. If we
			// spawned mid-phase, RegisterMember immediately switches us into the right behavior.
			if (Building != null)
				Building.RegisterMember(this);
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (HasStateAuthority && Building != null)
			{
				if (_retreatExit != null) _retreatExit.ReleaseClaim(this);
				Building.UnregisterMember(this);
			}
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

			if (_agent.enabled == false) return;

			// The agent can end up OFF the navmesh — knocked back (NPC base moves the transform directly),
			// shoved to a mesh edge by players, or left standing on a cell a closing exit's NavMeshObstacle
			// just carved away at dusk. Without recovery the early-out below would bail every tick and the NPC
			// would freeze forever. Snap it back onto the nearest navmesh first; only wait a tick if there's
			// genuinely no mesh nearby to stand on.
			if (_agent.isOnNavMesh == false)
			{
				if (NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 3f, NavMesh.AllAreas))
					_agent.Warp(navHit.position);
				if (_agent.isOnNavMesh == false) return;
			}

			// Default to moving each tick; the passive crowd-yield re-stops us while a player is close.
			_agent.isStopped = false;
			Holding          = false;

			// Restart the no-progress watchdog whenever a fresh pursuit begins (a path issued after idling),
			// so each new destination gets a clean window before AgentStuck() can abandon it.
			bool pursuing = _agent.hasPath && _agent.isStopped == false;
			if (pursuing && _wasPursuing == false) ResetProgressWatch();
			_wasPursuing = pursuing;

			Player target = (Hostile && Attack != null) ? FindClosestPlayerInAggro() : null;

			switch (_state)
			{
				case RuntimeState.Passive:    TickPassive(target);    break;
				case RuntimeState.Chase:      TickChase(target);      break;
				case RuntimeState.ReturnHome: TickReturnHome(target); break;
				case RuntimeState.Retreat:    TickRetreat();          break;
				case RuntimeState.Guard:      TickGuard();            break;
				case RuntimeState.GuardChase: TickGuardChase();       break;
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

			// Townsfolk stop dead while a player is close instead of walking into / through them.
			if (UpdateCrowdYield()) return;

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
				// Arrived, or pinned against a dead-end path (e.g. a wander point the bake routes through a
				// window the body can't pass) — drop it and idle, then pick a fresh point next time.
				if (HasArrived() || AgentStuck())
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
			else if (AgentStuck())
			{
				// Can't reach this waypoint (blocked) — skip to the next so the route doesn't freeze here.
				_agent.ResetPath();
				_patrolIndex = Path.NextIndex(_patrolIndex, PatrolMode, ref _patrolDir);
				GoToCurrentWaypoint();
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
			// Reached home, or can't path the rest of the way — settle into the passive routine either way
			// rather than freezing partway back.
			if (HasArrived() || AgentStuck()) EnterPassive();
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

		// ─── Building: dusk retreat & night defense ───────────────────────────
		// Driven by NpcBuilding off the match phase. All authority-only (NPC AI never runs on proxies).

		/// <summary>DuskWarning: drop whatever we were doing, claim an exit to shut, and head home.</summary>
		public void BeginRetreat()
		{
			if (HasStateAuthority == false) return;

			AttendedPlayer = PlayerRef.None;
			_wasAttending  = false;
			_resumeTimer   = default;

			_state       = RuntimeState.Retreat;
			_agent.speed = RetreatSpeed;
			if (_agent.enabled == false || _agent.isOnNavMesh == false) return;

			_retreatExit = Building != null ? Building.ClaimNextOpenExit(this) : null;
			if (_retreatExit != null) _agent.SetDestination(_retreatExit.CloseStandPoint);
			else                      GoToInterior();
		}

		/// <summary>Night: the household turns hostile and defends its zone.</summary>
		public void BeginHostileGuard()
		{
			if (HasStateAuthority == false) return;

			AttendedPlayer = PlayerRef.None;
			_wasAttending  = false;
			_resumeTimer   = default;

			if (_retreatExit != null) { _retreatExit.ReleaseClaim(this); _retreatExit = null; }
			EnterGuard();
		}

		/// <summary>Day/Lobby: resume the normal passive routine.</summary>
		public void ResumePassive()
		{
			if (HasStateAuthority == false) return;
			if (_retreatExit != null) { _retreatExit.ReleaseClaim(this); _retreatExit = null; }
			EnterPassive();
		}

		private void TickRetreat()
		{
			if (Building == null) { EnterPassive(); return; }

			if (_retreatExit != null)
			{
				// A player may have smashed/pried this door before we reached it — drop it and move on.
				if (_retreatExit.IsBreached || _retreatExit.IsClosed)
				{
					_retreatExit.ReleaseClaim(this);
					AdvanceRetreat();
					return;
				}

				if (_agent.hasPath == false) _agent.SetDestination(_retreatExit.CloseStandPoint);
				if (HasArrived())
				{
					_retreatExit.AuthorityClose();
					_retreatExit.ReleaseClaim(this);
					AdvanceRetreat();
				}
				else if (AgentStuck())
				{
					// Can't path to this exit — release it for another townsperson and move to the next job.
					_retreatExit.ReleaseClaim(this);
					AdvanceRetreat();
				}
				return;
			}

			// Nothing left to close — settle inside and start guarding (give up pathing if pinned).
			if (HasArrived() || AgentStuck()) EnterGuard();
		}

		private void AdvanceRetreat()
		{
			_retreatExit = Building.ClaimNextOpenExit(this);
			if (_retreatExit != null) _agent.SetDestination(_retreatExit.CloseStandPoint);
			else                      GoToInterior();
		}

		private void GoToInterior()
		{
			if (_agent.isOnNavMesh) _agent.SetDestination(Building.GetRetreatPoint(this));
		}

		private void EnterGuard()
		{
			_state       = RuntimeState.Guard;
			_agent.speed = MoveSpeed;
			if (_agent.isOnNavMesh) _agent.ResetPath();
			_idleTimer   = TickTimer.CreateFromSeconds(Runner, 0f);
		}

		private void TickGuard()
		{
			if (Building == null) { EnterPassive(); return; }

			// Territorial only at night: attack any player who enters the defense zone.
			if (Building.IsHostile && FindClosestPlayerInDefense() != null)
			{
				EnterGuardChase();
				return;
			}

			// Otherwise wander inside the interior.
			TickWanderWithin(Building.InteriorArea);
		}

		private void EnterGuardChase()
		{
			_state               = RuntimeState.GuardChase;
			_agent.speed         = ChaseSpeed;
			_outOfRangeTimer     = default;
			_lastChaseRepathTick = (int)Runner.Tick - ChaseRepathTicks;
		}

		private void TickGuardChase()
		{
			var zone   = Building != null ? Building.DefenseZone : null;
			var target = FindClosestPlayerInDefense();

			// Detection is gated on the zone, so a target leaving it reads as null here — the NPC never
			// chases beyond its house. Wait out the grace window, then give up and resume guarding.
			if (target == null)
			{
				if (_outOfRangeTimer.IsRunning == false)
					_outOfRangeTimer = TickTimer.CreateFromSeconds(Runner, ResetGraceSeconds);
				else if (_outOfRangeTimer.Expired(Runner))
					EnterGuard();
				return;
			}

			_outOfRangeTimer = default;
			FaceTarget(target.transform.position);

			if ((int)Runner.Tick - _lastChaseRepathTick >= ChaseRepathTicks)
			{
				// Clamp the chase destination into the zone so the NPC holds the line at its border
				// rather than stepping outside the house to reach a target hugging the edge.
				Vector3 dest = zone != null ? zone.ClampInside(target.transform.position) : target.transform.position;
				_agent.SetDestination(dest);
				_lastChaseRepathTick = (int)Runner.Tick;
			}

			float range   = Attack != null ? Attack.EffectiveRange : 0f;
			float distSqr = (target.transform.position - transform.position).sqrMagnitude;
			if (range > 0f && distSqr <= range * range) TryAttack();
		}

		private void TickWanderWithin(NpcMovementArea area)
		{
			if (_agent.hasPath)
			{
				if (HasArrived() || AgentStuck())
				{
					_agent.ResetPath();
					_idleTimer = TickTimer.CreateFromSeconds(Runner, RandRange(WanderIdleMin, WanderIdleMax));
				}
				return;
			}

			if (_idleTimer.ExpiredOrNotRunning(Runner))
			{
				if (area != null && area.TryRandomPoint(_rng, out Vector3 dest))
					_agent.SetDestination(dest);
				else
					_idleTimer = TickTimer.CreateFromSeconds(Runner, 0.5f);
			}
		}

		/// <summary>Closest living player inside the building's defense zone, or null. The AggroRadius
		/// overlap is the broad-phase cull — size it to cover the defense zone from where the NPC guards.</summary>
		private Player FindClosestPlayerInDefense()
		{
			if (Building == null) return null;
			var zone = Building.DefenseZone;
			if (zone == null) return null;

			int count = Physics.OverlapSphereNonAlloc(
				transform.position, AggroRadius, _overlapBuffer, CombatAction.HitMask, QueryTriggerInteraction.Ignore);

			Player closest   = null;
			float closestSqr = float.MaxValue;
			for (int i = 0; i < count; i++)
			{
				var player = _overlapBuffer[i].GetComponentInParent<Player>();
				if (player == null) continue;
				if (player.Health == null || player.Health.IsAlive == false) continue;
				if (zone.Contains(player.transform.position) == false) continue;

				float sqr = (player.transform.position - transform.position).sqrMagnitude;
				if (sqr < closestSqr) { closest = player; closestSqr = sqr; }
			}
			return closest;
		}

		// ─── Helpers ──────────────────────────────────────────────────────────

		private bool HasArrived()
		{
			if (_agent.pathPending) return false;
			return _agent.remainingDistance <= Mathf.Max(ArrivalTolerance, _agent.stoppingDistance);
		}

		private void ResetProgressWatch()
		{
			_progressSamplePos = transform.position;
			_progressTimer     = TickTimer.CreateFromSeconds(Runner, ProgressSampleSeconds);
		}

		/// <summary>
		/// True when the agent holds a path but has barely moved across a full sample window — i.e. it's pinned
		/// (a path the navmesh bake routes through a window/gap the body can't traverse, or shoved against
		/// geometry). The destination-driven states use this to abandon a dead-end path instead of freezing on
		/// it forever. Always check it AFTER <see cref="HasArrived"/> (<c>HasArrived() || AgentStuck()</c>) so a
		/// legitimate slow-down into the stopping distance never reads as stuck.
		/// </summary>
		private bool AgentStuck()
		{
			if (_agent.pathPending) return false;
			if (_progressTimer.ExpiredOrNotRunning(Runner) == false) return false;

			bool moved         = (transform.position - _progressSamplePos).sqrMagnitude >= ProgressEpsilonSqr;
			_progressSamplePos = transform.position;
			_progressTimer     = TickTimer.CreateFromSeconds(Runner, ProgressSampleSeconds);
			return moved == false;
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

		/// <summary>
		/// Single close-range behavior: while a player is inside <see cref="PersonalSpaceRadius"/> the NPC
		/// stops dead and stands idle; it only resumes once they pass a slightly wider exit radius
		/// (<see cref="CrowdHysteresis"/>). The hysteresis latch is what stops the idle↔walk flicker —
		/// without it the NPC re-decides every tick at the exact boundary. Returns true while stopped so
		/// the caller skips the normal state machine for the tick.
		/// </summary>
		private bool UpdateCrowdYield()
		{
			if (PersonalSpaceRadius <= 0f) { _crowdYielding = false; return false; }

			// Wider radius to LEAVE the yield than to ENTER it — pure hysteresis, kills boundary flicker.
			float radius   = _crowdYielding ? PersonalSpaceRadius + CrowdHysteresis : PersonalSpaceRadius;
			_crowdYielding = FindNearbyPlayer(radius) != null;
			if (_crowdYielding == false) return false;

			// Player is close: hold still. Mark Holding so Render keeps the idle pose even though the push
			// below may slide us. Clear the path, stop the agent, and zero velocity so it doesn't glide.
			Holding = true;
			if (_agent.enabled && _agent.isOnNavMesh)
			{
				if (_agent.hasPath) _agent.ResetPath();
				_agent.isStopped = true;
				_agent.velocity  = Vector3.zero;
			}

			// Stopped, but still shovable: let players physically ease us aside while we hold.
			ApplyPlayerPush();

			// Pause a beat after they leave rather than darting off, and don't strand a patrol mid-wait.
			_idleTimer         = TickTimer.CreateFromSeconds(Runner, RandRange(WanderIdleMin, WanderIdleMax));
			_waitingAtWaypoint = false;
			if (LookAround)
				transform.Rotate(0f, LookAroundSpeed * Runner.DeltaTime, 0f);
			return true;
		}

		/// <summary>
		/// Closest living player within <paramref name="radius"/> in any direction, or null. Used to make the
		/// NPC stop and idle while a player crowds it, so it never tries to walk through them.
		///
		/// Iterates <see cref="Player.All"/> rather than a physics overlap: the player's only PhysX
		/// collider is its SimpleKCC capsule on a dedicated (non-Default) layer, so an OverlapSphere
		/// against the combat HitMask never sees it. The roster is the authoritative, layer-agnostic
		/// source — and it includes bots.
		/// </summary>
		private Player FindNearbyPlayer(float radius)
		{
			float radiusSqr = radius * radius;
			Player closest   = null;
			float closestSqr = float.MaxValue;

			var all = Player.All;
			for (int i = 0; i < all.Count; i++)
			{
				var player = all[i];
				if (player == null) continue;
				if (player.Health != null && player.Health.IsAlive == false) continue;

				Vector3 to = player.transform.position - transform.position;
				to.y = 0f;
				float sqr = to.sqrMagnitude;
				if (sqr > radiusSqr) continue;
				if (sqr < closestSqr) { closest = player; closestSqr = sqr; }
			}
			return closest;
		}

		/// <summary>
		/// Physically eases the (already-stopped) NPC away from any player inside <see cref="PushRadius"/>,
		/// summing an away-vector per player and applying it through <c>NavMeshAgent.Move</c> so the slide
		/// stays on the navmesh. Called only while Holding, so the body slides but the animation stays idle.
		/// Iterates <see cref="Player.All"/> for the same reason as <see cref="FindNearbyPlayer"/>.
		/// </summary>
		private void ApplyPlayerPush()
		{
			if (PushStrength <= 0f || PushRadius <= 0f) return;
			if (_agent.enabled == false || _agent.isOnNavMesh == false) return;

			float radiusSqr = PushRadius * PushRadius;
			Vector3 push    = Vector3.zero;

			var all = Player.All;
			for (int i = 0; i < all.Count; i++)
			{
				var player = all[i];
				if (player == null) continue;
				if (player.Health != null && player.Health.IsAlive == false) continue;

				Vector3 away = transform.position - player.transform.position;
				away.y = 0f;
				float sqr = away.sqrMagnitude;
				if (sqr > radiusSqr || sqr < 0.0001f) continue;

				float dist = Mathf.Sqrt(sqr);
				push += (away / dist) * (1f - dist / PushRadius);
			}

			if (push.sqrMagnitude > 0.0001f)
				_agent.Move(Vector3.ClampMagnitude(push, 1f) * PushStrength * Runner.DeltaTime);
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

			// While holding for a nearby player, force the idle pose even though a push may be sliding the
			// body — otherwise the position delta reads as "walking" on every client.
			if (Holding) speed = 0f;

			// Damp the locomotion blend so a brief stop/start can't snap the animation between idle and walk.
			_animator.SetFloat(_hashSpeed,       speed, SpeedDampSeconds, dt);
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
