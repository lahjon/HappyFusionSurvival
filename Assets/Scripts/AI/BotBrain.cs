using Fusion;
using Fusion.Addons.SimpleKCC;
using UnityEngine;
using UnityEngine.AI;
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
	/// Lives on the Player prefab alongside <see cref="BotBrain"/>'s sibling components; only matters when the Player
	/// was spawned via <see cref="GameManager.AddBots"/> (i.e. <see cref="Player.IsBot"/> is true).
	/// </summary>
	public sealed class BotBrain : NetworkBehaviour
	{
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

		// Cached siblings / managers (host only).
		private Player _player;
		private SimpleKCC _kcc;
		private Inventory _inventory;
		private ActionInvoker _invoker;
		private NavMeshAgent _agent;
		private GameManager _gameManager;

		// Synthetic input state.
		private GameplayInput _currentInput;
		private NetworkButtons _prevButtons;
		private Vector2 _aimLook;          // (pitch, yaw) in degrees — the running look the bot drives the KCC with

		// Behaviour bookkeeping (authority-local).
		private System.Random _rng;
		private Vector3 _home;
		private bool _armed;
		private float _nextRepathTime;
		private Vector3 _wanderDest;
		private bool _haveWanderDest;
		private float _wanderIdleUntil;

		/// <summary>True when this brain should drive the Player (it's a bot, on the authority). Read by
		/// <see cref="Player.TryGetTickInput"/>.</summary>
		public bool IsActive => _player != null && _player.IsBot && Object != null && Object.HasStateAuthority;

		public override void Spawned()
		{
			_player = GetComponent<Player>();
			_inventory = GetComponent<Inventory>();
			_invoker = GetComponent<ActionInvoker>();
			_kcc = _player != null ? _player.KCC : null;

			// Only an authoritative bot needs a brain — and a NavMeshAgent. Humans and proxies get neither: the
			// agent is added here at runtime, exclusively for bots, so it can never touch a normal player's
			// SimpleKCC movement (which was the whole problem with baking it onto the shared Player prefab).
			if (HasStateAuthority == false || _player == null || _player.IsBot == false)
				return;

			_gameManager = FindAnyObjectByType<GameManager>();
			_home = _kcc != null ? _kcc.Position : transform.position;
			_aimLook = new Vector2(0f, _player.transform.eulerAngles.y);
			_rng = new System.Random(unchecked((int)(Object.Id.Raw * 2654435761u + 1013904223u)));

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

			Vector3 eye = _player.CameraHandle != null ? _player.CameraHandle.position : _kcc.Position + Vector3.up * 1.5f;
			Vector3 aimPoint = target.transform.position + Vector3.up * TargetAimHeight;
			Vector3 toTarget = aimPoint - eye;

			// Aim: ease the running look toward the target bearing.
			Vector2 desired = LookToward(toTarget);
			float dt = Runner.DeltaTime;
			_aimLook.x = Mathf.MoveTowardsAngle(_aimLook.x, desired.x, AimTurnSpeed * dt);
			_aimLook.y = Mathf.MoveTowardsAngle(_aimLook.y, desired.y, AimTurnSpeed * dt);
			input.LookRotation = _aimLook;

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
				if (dist > SprintDistance)
					input.Buttons.Set(EInputButton.Sprint, true);
			}

			// Fire: aimed at the target and within range. CanFire (the weapon cooldown) naturally pulses the
			// button — after a shot CanFire is false, so Fire releases and re-presses (a fresh edge) when ready.
			bool aimed = Mathf.Abs(Mathf.DeltaAngle(_aimLook.y, desired.y)) <= AimToleranceDeg
			          && Mathf.Abs(Mathf.DeltaAngle(_aimLook.x, desired.x)) <= AimToleranceDeg;
			bool inRange = dist <= fireRange;
			if (aimed && inRange && _invoker != null && _invoker.CanFire)
				input.Buttons.Set(EInputButton.Fire, true);

			return input;
		}

		private Player AcquireTarget()
		{
			if (_gameManager == null) _gameManager = FindAnyObjectByType<GameManager>();
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

			float now = Time.time;
			if (_haveWanderDest == false)
			{
				if (now >= _wanderIdleUntil && TryPickWanderDestination(out _wanderDest, leashToHome))
					_haveWanderDest = true;
				else
					return input; // idling
			}

			Vector3 flat = _wanderDest - _kcc.Position;
			flat.y = 0f;
			if (flat.magnitude <= 1f)
			{
				// Arrived — idle, then pick a new spot.
				_haveWanderDest = false;
				_wanderIdleUntil = now + RandRange(WanderIdleMin, WanderIdleMax);
				return input;
			}

			Vector3 worldDir = SteerToward(_wanderDest, flat);
			if (worldDir.sqrMagnitude > 0.001f)
			{
				// Face the direction of travel while wandering.
				_aimLook.y = Mathf.MoveTowardsAngle(_aimLook.y, Mathf.Atan2(worldDir.x, worldDir.z) * Mathf.Rad2Deg, AimTurnSpeed * Runner.DeltaTime);
				_aimLook.x = Mathf.MoveTowardsAngle(_aimLook.x, 0f, AimTurnSpeed * Runner.DeltaTime);
				input.LookRotation = _aimLook;
				input.MoveDirection = WorldToLocalMove(worldDir);
			}
			return input;
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
				if (Time.time >= _nextRepathTime)
				{
					_agent.SetDestination(destPos);
					_nextRepathTime = Time.time + RepathInterval;
				}

				Vector3 steer = _agent.desiredVelocity;
				steer.y = 0f;
				if (steer.sqrMagnitude > 0.04f)
					return steer.normalized;
			}

			// Fallback: straight at the target.
			return flatToTarget.sqrMagnitude > 0.001f ? flatToTarget.normalized : Vector3.zero;
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

		private float RandRange(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
	}
}
