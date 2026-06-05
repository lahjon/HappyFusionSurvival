using Fusion;
using Fusion.Addons.SimpleKCC;
using UnityEngine;
using UnityEngine.AI;
using Starter.Common.Interactions;
using Starter.Common.Inventory;

namespace Starter.Shooter
{
	/// <summary>
	/// Host-authoritative AI that drives a bot-controlled <see cref="Player"/>. It does NOT move the body itself —
	/// instead it synthesises a <see cref="GameplayInput"/> each tick that the Player consumes through the exact same
	/// pipeline as a human (see <see cref="Player.TryGetTickInput"/>). So a bot reuses all of the real movement,
	/// stamina, and combat code; this class only decides "where to look, where to move, when to fire".
	///
	/// <para><b>Planning vs. motion.</b> A <see cref="NavMeshAgent"/> in planning-only mode
	/// (<c>updatePosition/updateRotation = false</c>) computes a path; we read <see cref="NavMeshAgent.desiredVelocity"/>
	/// for steering and feed it into <c>input.MoveDirection</c>. The SimpleKCC on the Player does the actual moving.
	/// The agent is <b>added at runtime, only for bots</b> (see <see cref="Spawned"/>) — human players and proxies
	/// never get one, so it can never interfere with normal KCC movement. If no NavMesh is baked, the bot falls back
	/// to steering straight at its target.</para>
	///
	/// <para><b>Runs only on the state authority.</b> Proxies never simulate a bot (no input authority), so the brain
	/// is dormant there. <see cref="ProduceInput"/> is pulled synchronously from <see cref="Player.FixedUpdateNetwork"/>,
	/// so there is no tick-ordering race — the brain "thinks" exactly when the Player asks for input.</para>
	///
	/// <para><b>Plain <see cref="MonoBehaviour"/> by design.</b> The brain holds <i>no</i> <c>[Networked]</c> state — it
	/// only reads siblings and emits a local input struct — so it is deliberately NOT a <see cref="NetworkBehaviour"/>.
	/// That keeps it out of the Fusion bake entirely (no behaviour-index / state-word allocation, no bake corruption
	/// risk when the component is added to the prefab) and lets <see cref="Player"/> own its lifecycle via
	/// <see cref="Initialize"/> instead of a networked <c>Spawned()</c> callback.</para>
	///
	/// <para><b>Runtime-attached, bots only.</b> The shared Player prefab carries NO BotBrain — a human player is not
	/// an AI. <see cref="Player.Spawned"/> calls <c>gameObject.AddComponent&lt;BotBrain&gt;()</c> + <see cref="Initialize"/>
	/// on the host exclusively when <see cref="Player.IsBot"/> is true (bots spawned via <see cref="GameManager.AddBots"/>).
	/// Humans and proxies therefore never have this component at all.</para>
	/// </summary>
	public sealed class BotBrain : MonoBehaviour
	{
		[Header("Difficulty")]
		[Tooltip("Skill tier — set by GameManager.AddBots → Player.ConfigureAsBot before Initialize. Drives fire rate " +
			"(fraction of the weapon's natural cadence) and aim accuracy (share of shots aimed to hit).")]
		public BotDifficulty Difficulty = BotDifficulty.Medium;
		[Tooltip("Sideways/vertical distance (metres) a deliberately-missed shot is aimed off the target's body. Large " +
			"enough to clear the player capsule at any range, so a 'miss' roll is a geometric miss regardless of distance.")]
		[Min(0.5f)] public float MissOffsetMeters = 2f;

		[Header("Aim")]
		[Tooltip("How fast the bot swings its aim toward a target (degrees/second).")]
		[Min(30f)] public float AimTurnSpeed = 220f;
		[Tooltip("Bot only fires once its aim is within this many degrees of the target.")]
		[Min(1f)] public float AimToleranceDeg = 12f;
		[Tooltip("Vertical offset above a target's root that the bot aims at (chest height).")]
		public float TargetAimHeight = 1.2f;

		[Header("Movement")]
		[Tooltip("Bot stops closing in once within this fraction of its weapon's effective range.")]
		[Range(0.3f, 1f)] public float StopRangeFactor = 0.8f;
		[Tooltip("Below this distance to the target the bot holds position instead of repathing (avoids jitter).")]
		[Min(0.5f)] public float MeleeHoldDistance = 1.6f;
		[Tooltip("Beyond this distance to the target the bot sprints to close the gap.")]
		[Min(2f)] public float SprintDistance = 8f;
		[Tooltip("Seconds between navmesh repaths while chasing.")]
		[Min(0.05f)] public float RepathInterval = 0.2f;

		[Header("Day wander")]
		[Tooltip("Radius around the spawn point the bot wanders within during the Day phase.")]
		[Min(1f)] public float WanderRadius = 10f;
		[Tooltip("Min/max idle pause (seconds) between wander destinations.")]
		public float WanderIdleMin = 1.5f;
		public float WanderIdleMax = 4f;
		[Tooltip("Abandon a wander destination this many seconds after picking it, so the bot never pins forever against unreachable geometry.")]
		[Min(1f)] public float WanderGiveUpSeconds = 6f;

		[Header("Doors")]
		[Tooltip("How far ahead (metres) the bot probes for a closed door in its path and opens it.")]
		[Min(0.5f)] public float DoorProbeDistance = 1.6f;

		// Cached siblings / managers (host only).
		private Player _player;
		private NetworkObject _object;
		private NetworkRunner _runner;
		private SimpleKCC _kcc;
		private Inventory _inventory;
		private ActionInvoker _invoker;
		private NavMeshAgent _agent;
		private GameManager _gameManager;

		// Synthetic input state.
		private GameplayInput _currentInput;
		private NetworkButtons _prevButtons;
		private Vector2 _aimLook;          // (pitch, yaw) in degrees — the running look the bot drives the KCC with

		// Difficulty-derived tuning (resolved once in Initialize from Difficulty).
		private float _fireRateFactor;   // shot cadence as a fraction of the weapon's natural rate (1 = full)
		private float _hitRate;          // probability a planned shot is aimed dead-on (rest aimed to miss)
		private bool _aimsAtTarget;      // false for Retarded — fires in random directions, never tracks the enemy

		// Per-shot firing state (authority-local).
		private float _nextFireTime;     // sim time the next trigger pull is allowed
		private bool _shotPlanned;       // hit/miss has been rolled for the pending shot (aim committed)
		private Vector3 _shotAimOffset;  // world offset added to the target aim point this shot (zero = hit, off-body = miss)
		private Vector3 _randomAimDir;   // Retarded: the random world bearing committed for this shot

		// Behaviour bookkeeping (authority-local).
		private System.Random _rng;
		private Vector3 _home;
		private bool _armed;
		private float _nextRepathTime;
		private Vector3 _wanderDest;
		private bool _haveWanderDest;
		private float _wanderIdleUntil;
		private float _wanderDeadline;       // give up on the current dest at this sim time
		private Vector3 _wanderProgressPos;  // body position at the last progress sample
		private float _wanderProgressAt;     // next sim time to sample wander progress

		/// <summary>True when this brain should drive the Player (it's a bot, on the authority). Read by
		/// <see cref="Player.TryGetTickInput"/>.</summary>
		public bool IsActive => _player != null && _player.IsBot && _object != null && _object.HasStateAuthority;

		/// <summary>Re-tune this bot's skill tier at runtime (e.g. the <c>bot_difficulty</c> console command) and
		/// re-derive its fire-rate / accuracy profile. Safe to call before or after <see cref="Initialize"/>.</summary>
		public void SetDifficulty(BotDifficulty difficulty)
		{
			Difficulty = difficulty;
			ApplyDifficultyProfile();
			_shotPlanned = false; // re-plan the pending shot under the new accuracy
		}

		private void ApplyDifficultyProfile() => Difficulty.GetProfile(out _fireRateFactor, out _hitRate, out _aimsAtTarget);

		/// <summary>Called by <see cref="Player.Spawned"/> on the state authority once the Player is spawned and its KCC
		/// is positioned. Caches siblings and — only for an authoritative bot — spins up the planning NavMeshAgent.
		/// This is a plain MonoBehaviour (no networked state), so the owning Player drives its lifecycle instead of a
		/// Fusion <c>Spawned()</c> callback.</summary>
		public void Initialize()
		{
			_player = GetComponent<Player>();
			_inventory = GetComponent<Inventory>();
			_invoker = GetComponent<ActionInvoker>();
			_object = _player != null ? _player.Object : GetComponent<NetworkObject>();
			_runner = _object != null ? _object.Runner : null;
			_kcc = _player != null ? _player.KCC : null;

			// Only an authoritative bot needs a brain — and a NavMeshAgent. Humans and proxies get neither: the
			// agent is added here at runtime, exclusively for bots, so it can never touch a normal player's
			// SimpleKCC movement (which was the whole problem with baking it onto the shared Player prefab).
			if (_object == null || _object.HasStateAuthority == false || _player == null || _player.IsBot == false)
				return;

			_gameManager = GameManager.Instance;
			_home = _kcc != null ? _kcc.Position : transform.position;
			_aimLook = new Vector2(0f, _player.transform.eulerAngles.y);
			_rng = new System.Random(unchecked((int)(_object.Id.Raw * 2654435761u + 1013904223u)));
			ApplyDifficultyProfile();

			// Planning-only agent: computes paths but never drives the transform — the KCC moves the body.
			_agent = gameObject.AddComponent<NavMeshAgent>();
			_agent.updatePosition = false;
			_agent.updateRotation = false;
			WarpAgentToBody();
		}

		/// <summary>Called by <see cref="Player.FixedUpdateNetwork"/> via the input seam. Computes this tick's input
		/// and reports the previous tick's buttons for edge detection.</summary>
		public bool ProduceInput(out GameplayInput input, out NetworkButtons previousButtons)
		{
			previousButtons = _prevButtons;
			_currentInput = Think();
			_prevButtons = _currentInput.Buttons;
			input = _currentInput;
			return true;
		}

		private GameplayInput Think()
		{
			var input = new GameplayInput { LookRotation = _aimLook };
			if (_player == null) return input;

			var phase = MatchManager.Instance != null ? MatchManager.Instance.Phase : MatchPhase.Lobby;

			if (phase == MatchPhase.Night)
			{
				// Bots can't reach a team zone the way humans do, so arm them once Night starts.
				if (_armed == false)
				{
					_player.SetPvpArmed(true);
					_armed = true;
				}

				var target = AcquireTarget();
				if (target != null)
					return ThinkCombat(target);

				// No enemy left alive / visible — hold and look around.
				return Wander(input, leashToHome: false);
			}

			_armed = false;
			return Wander(input, leashToHome: true);
		}

		// ─── Night combat ────────────────────────────────────────────────────

		private GameplayInput ThinkCombat(Player target)
		{
			var input = new GameplayInput();
			float now = (float)_runner.SimulationTime;
			float dt = _runner.DeltaTime;

			Vector3 eye = _player.CameraHandle != null ? _player.CameraHandle.position : _kcc.Position + Vector3.up * 1.5f;

			// Commit this shot's aim (a single hit/miss roll) once the previous shot's cadence has elapsed, so the bot
			// settles its look on one committed bearing instead of re-rolling every tick.
			if (_shotPlanned == false && now >= _nextFireTime)
				PlanShot(target, eye);

			// Where the bot is currently trying to point. Retarded ignores the target and aims at a random bearing;
			// the others aim at the body plus this shot's offset (zero on a hit roll, off-body on a miss roll).
			Vector3 toAim = _aimsAtTarget
				? (target.transform.position + Vector3.up * TargetAimHeight + _shotAimOffset) - eye
				: _randomAimDir;

			Vector2 desired = LookToward(toAim);
			_aimLook.x = Mathf.MoveTowardsAngle(_aimLook.x, desired.x, AimTurnSpeed * dt);
			_aimLook.y = Mathf.MoveTowardsAngle(_aimLook.y, desired.y, AimTurnSpeed * dt);
			input.LookRotation = _aimLook;

			// Movement always uses the REAL target position — even a wild-firing bot still advances on the enemy.
			float fireRange = ActiveActionRange();
			Vector3 flatToTarget = target.transform.position - _kcc.Position;
			flatToTarget.y = 0f;
			float dist = flatToTarget.magnitude;

			// Move: close the gap until within firing range; hold once close enough.
			float stopDist = Mathf.Max(MeleeHoldDistance, fireRange * StopRangeFactor);
			if (dist > stopDist)
			{
				Vector3 worldDir = SteerToward(target.transform.position, flatToTarget);
				input.MoveDirection = WorldToLocalMove(worldDir);
				TryOpenBlockingDoor(worldDir);
				if (dist > SprintDistance)
					input.Buttons.Set(EInputButton.Sprint, true);
			}

			// Fire when the planned shot is due, the aim has settled on its (possibly offset / random) bearing, the
			// target is in range and the weapon is off cooldown. Setting the button for exactly the firing tick gives
			// ProcessFireInput a clean press edge; clearing _shotPlanned releases it next tick so the following shot
			// re-rolls hit/miss and re-presses.
			bool aimed = Mathf.Abs(Mathf.DeltaAngle(_aimLook.y, desired.y)) <= AimToleranceDeg
			          && Mathf.Abs(Mathf.DeltaAngle(_aimLook.x, desired.x)) <= AimToleranceDeg;
			bool inRange = dist <= fireRange;
			if (_shotPlanned && aimed && inRange && now >= _nextFireTime && _invoker != null && _invoker.CanFire)
			{
				input.Buttons.Set(EInputButton.Fire, true);
				// Pace the next trigger pull by the difficulty's share of the weapon's natural cadence: Hard fires every
				// cooldown, Medium every other, Easy/Retarded slower still.
				_nextFireTime = now + ActiveActionCooldown() / Mathf.Max(0.01f, _fireRateFactor);
				_shotPlanned = false;
			}

			return input;
		}

		// Commit the next shot's aim: roll hit vs. miss (per the difficulty's hit rate) and stash the resulting aim
		// offset. A miss is aimed a couple of metres off the target body so it misses at any range; a Retarded bot picks
		// a fully random bearing and never tracks the enemy.
		private void PlanShot(Player target, Vector3 eye)
		{
			_shotPlanned = true;

			if (_aimsAtTarget == false)
			{
				float yaw = (float)_rng.NextDouble() * 360f;
				float pitch = ((float)_rng.NextDouble() * 2f - 1f) * 20f; // a little up/down spray, mostly level
				_randomAimDir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
				return;
			}

			if (_rng.NextDouble() < _hitRate)
			{
				_shotAimOffset = Vector3.zero; // aimed to hit
				return;
			}

			// Aimed to miss: push the aim point off the body in a random direction around the line of sight.
			Vector3 los = target.transform.position - eye;
			los.y = 0f;
			Vector3 right = los.sqrMagnitude > 0.001f
				? Vector3.Cross(Vector3.up, los.normalized)
				: Vector3.right;
			float ang = (float)_rng.NextDouble() * Mathf.PI * 2f;
			_shotAimOffset = (Mathf.Cos(ang) * right + Mathf.Sin(ang) * Vector3.up) * MissOffsetMeters;
		}

		private Player AcquireTarget()
		{
			if (_gameManager == null) _gameManager = GameManager.Instance;
			var roster = _gameManager != null ? _gameManager.Players : null;
			if (roster == null) return null;

			var teams = TeamManager.Instance;
			int myTeam = teams != null ? teams.TeamOf(_player.Owner) : -1;

			Player best = null;
			float bestSqr = float.MaxValue;
			Vector3 myPos = _kcc.Position;

			for (int i = 0; i < roster.Count; i++)
			{
				var p = roster[i];
				if (p == null || p == _player || p.Object == null) continue;
				if (p.Health == null || p.Health.IsAlive == false) continue;

				// Enemies only: skip same-team. With teams assigned, an unteamed actor (-1) is still fair game.
				if (myTeam >= 0 && teams != null && teams.TeamOf(p.Owner) == myTeam) continue;

				float sqr = (p.transform.position - myPos).sqrMagnitude;
				if (sqr < bestSqr) { bestSqr = sqr; best = p; }
			}
			return best;
		}

		// ─── Wander (Day / no target) ────────────────────────────────────────

		private GameplayInput Wander(GameplayInput input, bool leashToHome)
		{
			input.LookRotation = _aimLook;

			// Sim clock (not Time.time): the brain runs inside FixedUpdateNetwork on the host, so timing
			// must advance with the simulation tick to stay deterministic under re-simulation / time-scale.
			float now = (float)_runner.SimulationTime;
			if (_haveWanderDest == false)
			{
				if (now >= _wanderIdleUntil && TryPickWanderDestination(out _wanderDest, leashToHome))
				{
					_haveWanderDest = true;
					_wanderDeadline = now + WanderGiveUpSeconds;
					_wanderProgressPos = _kcc.Position;
					_wanderProgressAt = now + 1.5f;
				}
				else
				{
					return input; // idling
				}
			}

			Vector3 flat = _wanderDest - _kcc.Position;
			flat.y = 0f;
			if (flat.magnitude <= 1f)
			{
				// Arrived — idle, then pick a new spot.
				EndWanderDest(now);
				return input;
			}

			Vector3 worldDir = SteerToward(_wanderDest, flat);

			// Clearing a door in our path counts as making progress: the body naturally pauses for a beat against
			// the door before it swings open, so refresh the stuck timers while a door is right in front. Without
			// this the no-progress check abandons the destination the instant the bot reaches a door — it would
			// open the door and then stop instead of walking through.
			bool atDoor = TryOpenBlockingDoor(worldDir);
			if (atDoor)
			{
				_wanderProgressPos = _kcc.Position;
				_wanderProgressAt = now + 1.5f;
				_wanderDeadline = now + WanderGiveUpSeconds;
			}
			// Give up on a destination the bot can't reach so it never pins forever against geometry: a hard
			// timeout, plus a no-progress check (barely moved over the sample window → blocked). Without this the
			// very first unreachable pick (a wander point behind a wall, or an isolated navmesh island) latches
			// _haveWanderDest and the bot pushes into the obstacle for good — "moves once, then stops forever".
			else if (now >= _wanderDeadline || NoWanderProgress(now))
			{
				EndWanderDest(now);
				return input;
			}

			if (worldDir.sqrMagnitude > 0.001f)
			{
				// Face the direction of travel while wandering.
				_aimLook.y = Mathf.MoveTowardsAngle(_aimLook.y, Mathf.Atan2(worldDir.x, worldDir.z) * Mathf.Rad2Deg, AimTurnSpeed * _runner.DeltaTime);
				_aimLook.x = Mathf.MoveTowardsAngle(_aimLook.x, 0f, AimTurnSpeed * _runner.DeltaTime);
				input.LookRotation = _aimLook;
				input.MoveDirection = WorldToLocalMove(worldDir);
			}
			return input;
		}

		// Drop the current wander destination and schedule the next idle pause.
		private void EndWanderDest(float now)
		{
			_haveWanderDest = false;
			_wanderIdleUntil = now + RandRange(WanderIdleMin, WanderIdleMax);
		}

		// Sampled every ~1.5s while pursuing a destination: true when the bot has barely moved since the last
		// sample (i.e. it's blocked / pinned), so the caller can abandon the destination and pick another.
		private bool NoWanderProgress(float now)
		{
			if (now < _wanderProgressAt) return false;
			bool stuck = (_kcc.Position - _wanderProgressPos).sqrMagnitude < 0.25f; // < 0.5 m in the window
			_wanderProgressPos = _kcc.Position;
			_wanderProgressAt = now + 1.5f;
			return stuck;
		}

		private bool TryPickWanderDestination(out Vector3 result, bool leashToHome)
		{
			Vector3 center = leashToHome ? _home : _kcc.Position;
			float ang = (float)_rng.NextDouble() * Mathf.PI * 2f;
			float rad = Mathf.Sqrt((float)_rng.NextDouble()) * Mathf.Max(1f, WanderRadius);
			Vector3 candidate = center + new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);

			if (_agent != null && _agent.enabled && NavMesh.SamplePosition(candidate, out NavMeshHit hit, WanderRadius, NavMesh.AllAreas))
			{
				result = hit.position;
				return true;
			}
			// No navmesh — wander straight toward the raw candidate.
			result = candidate;
			return true;
		}

		// ─── Steering / conversions ──────────────────────────────────────────

		// Returns a horizontal world-space unit direction toward destPos, using the navmesh path when available
		// and falling back to a straight line. flatToTarget is the precomputed horizontal offset to the target.
		private Vector3 SteerToward(Vector3 destPos, Vector3 flatToTarget)
		{
			if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
			{
				WarpAgentToBody();
				float now = (float)_runner.SimulationTime;
				if (now >= _nextRepathTime)
				{
					_agent.SetDestination(destPos);
					_nextRepathTime = now + RepathInterval;
				}

				Vector3 steer = _agent.desiredVelocity;
				steer.y = 0f;
				if (steer.sqrMagnitude > 0.04f)
					return steer.normalized;
			}

			// Fallback: straight at the target.
			return flatToTarget.sqrMagnitude > 0.001f ? flatToTarget.normalized : Vector3.zero;
		}

		// If a door-like barrier sits directly ahead within reach, force it open when it's impassable. Bots are on
		// the state authority, so they shove through any IDoorBarrier directly (a Door opens, an NpcExit breaches)
		// rather than the human RPC/interaction path. The navmesh doesn't model doors, so this physical forward
		// probe is what actually clears them from the bot's path.
		//
		// Returns true while a door is right in front of the bot — whether it just forced it open or is walking
		// through an already-open one — so the wander stuck-detection treats "at a doorway" as progress and
		// doesn't abandon the destination mid-pass.
		private bool TryOpenBlockingDoor(Vector3 worldDir)
		{
			if (worldDir.sqrMagnitude < 0.0001f) return false;
			Vector3 origin = _kcc.Position + Vector3.up * 1.0f; // chest height — clears low thresholds/steps
			if (Physics.Raycast(origin, worldDir.normalized, out RaycastHit hit, DoorProbeDistance, ~0, QueryTriggerInteraction.Ignore))
			{
				var barrier = hit.collider.GetComponentInParent<IDoorBarrier>();
				if (barrier != null)
				{
					if (barrier.IsPassable == false) barrier.AuthorityForceOpen();
					return true;
				}
			}
			return false;
		}

		// Keep the planning agent co-located with the KCC body so its path/desiredVelocity stay relevant.
		private void WarpAgentToBody()
		{
			if (_agent == null || _agent.enabled == false) return;
			Vector3 pos = _kcc.Position;
			if (_agent.isOnNavMesh) _agent.nextPosition = pos;
			else if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 2f, NavMesh.AllAreas)) _agent.Warp(hit.position);
		}

		// Convert a world-space move direction into the look-relative (strafe, forward) space ProcessInput expects.
		private Vector2 WorldToLocalMove(Vector3 worldDir)
		{
			if (worldDir.sqrMagnitude < 0.0001f) return Vector2.zero;
			Vector3 local = Quaternion.Inverse(_kcc.TransformRotation) * worldDir;
			Vector2 move = new Vector2(local.x, local.z);
			return move.sqrMagnitude > 1f ? move.normalized : move;
		}

		// World direction → (pitch, yaw) degrees in the convention KCC.SetLookRotation expects (pitch<0 looks up).
		private static Vector2 LookToward(Vector3 dir)
		{
			float horiz = new Vector2(dir.x, dir.z).magnitude;
			float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
			float pitch = horiz > 0.0001f ? -Mathf.Atan2(dir.y, horiz) * Mathf.Rad2Deg : 0f;
			return new Vector2(pitch, yaw);
		}

		private float ActiveActionRange()
		{
			var action = _inventory != null ? _inventory.ActiveAction : null;
			float range = action != null ? action.EffectiveRange : 0f;
			return range > 0.1f ? range : MeleeHoldDistance; // fists report a short range; never return ~0
		}

		// Natural seconds-between-shots of the equipped weapon, used as the base cadence the fire-rate factor scales.
		private float ActiveActionCooldown()
		{
			var action = _inventory != null ? _inventory.ActiveAction : null;
			float cd = action != null ? action.Cooldown : 0f;
			return cd > 0.01f ? cd : 0.5f; // floor so a zero-cooldown action still paces bot shots
		}

		private float RandRange(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
	}
}
