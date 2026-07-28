using System.Collections.Generic;
using Fusion;
using Starter.City;
using Starter.Shooter;
using UnityEngine;
using UnityEngine.AI;

namespace Starter.Monsters
{
	/// <summary>
	/// One of the things in the city. Host-authoritative: the AI only ever runs on the state authority and the
	/// result is replicated as position plus a state byte, so clients never disagree about where a monster is or
	/// simulate a second, divergent copy of it.
	///
	/// <para><b>Not an enemy — a hazard.</b> There is no health, no damage, no aggro meter to whittle down.
	/// Contact kills. Every mechanic here is therefore about <em>information</em>: what it can see, how far, and
	/// how that changes when the player is on open ground versus inside. Combat systems would undermine the
	/// entire loop, so this component deliberately exposes no way to be hurt.</para>
	///
	/// <para>Sight range is scaled by cover rather than by a visibility raycast alone. A raycast answers "is
	/// there a wall in the way", which is not the question — the question is "am I standing in the middle of a
	/// four-lane avenue". Reading that from the city layout is both cheaper and closer to what players actually
	/// perceive as risk.</para>
	/// </summary>
	public sealed class Monster : NetworkBehaviour
	{
		[Header("Profile")]
		[Tooltip("Which type this is. The director assigns it at spawn; profiles live on MonsterDirector.")]
		[SerializeField] private MonsterType _type = MonsterType.Stalker;

		[Header("Motion")]
		[Tooltip("Turn rate while moving, degrees per second.")]
		[Min(30f)] public float TurnSpeed = 220f;

		[Tooltip("Distance at which a path waypoint counts as reached.")]
		[Min(0.5f)] public float WaypointTolerance = 2f;

		[Tooltip("Seconds between full re-paths while hunting. Repathing every tick is wasted work — the target " +
			"has not moved far enough to matter.")]
		[Min(0.1f)] public float RepathInterval = 1.5f;

		// =========================================================================
		// Networked
		// =========================================================================

		[Networked] public MonsterState State { get; private set; }
		[Networked] public MonsterType Type { get; private set; }

		/// <summary>Where it is trying to get to. Replicated mainly for debugging and audio cues.</summary>
		[Networked] public Vector3 Destination { get; private set; }

		// =========================================================================
		// Host-only state
		// =========================================================================

		private MonsterProfile _profile;
		private CityNavGraph _graph;
		private CityLayout _layout;
		private NavMeshPath _navPath;
		private readonly List<Vector3> _path = new List<Vector3>(32);
		private readonly List<Vector3> _streetScratch = new List<Vector3>(32);
		private int _pathCursor;
		private Vector3 _homePosition;

		// TickTimer, not Time.time. These are read inside FixedUpdateNetwork, which runs on the simulation clock
		// and — crucially — re-runs during resimulation. Wall-clock time keeps advancing across those repeats,
		// so a Time.time deadline resolves differently on each pass and the AI's decisions stop being a function
		// of tick state. Host-only fields, so they need no [Networked] attribute; they just need the right clock.
		private TickTimer _repathTimer;
		private TickTimer _loseInterestTimer;

		private Player _target;
		private RunRng _rng;
		private CharacterController _controller;

		public override void Spawned()
		{
			_controller = GetComponent<CharacterController>();
			if (HasStateAuthority)
			{
				Type = _type;
				_homePosition = transform.position;
			}
		}

		/// <summary>Host-only setup, called by <see cref="MonsterDirector"/> immediately after spawn.</summary>
		public void HostConfigure(MonsterType type, MonsterProfile profile, CityNavGraph graph, CityLayout layout, int seed, int index)
		{
			if (!HasStateAuthority) return;

			_type = type;
			Type = type;
			_profile = profile;
			_graph = graph;
			_layout = layout;
			_homePosition = transform.position;
			_rng = RunRng.For(seed, RngStream.Monsters, index);
			State = MonsterState.Patrol;
		}

		public override void FixedUpdateNetwork()
		{
			if (!HasStateAuthority || _profile == null) return;

			UpdateSenses();
			UpdateState();
			Move(Runner.DeltaTime);
			TryKill();
		}

		// =========================================================================
		// Senses
		// =========================================================================

		/// <summary>Picks the most detectable living player, or none. Runs every tick — it is a handful of
		/// distance checks against at most four players.</summary>
		private void UpdateSenses()
		{
			var gm = GameManager.Instance;
			if (gm == null) return;

			Player best = null;
			float bestScore = 0f;

			var players = gm.Players;
			for (int i = 0; i < players.Count; i++)
			{
				var player = players[i];
				if (player == null || player.Object == null) continue;
				if (player.Health == null || !player.Health.IsAlive) continue;

				float score = Detectability(player);
				if (score <= bestScore) continue;
				bestScore = score;
				best = player;
			}

			if (best != null)
			{
				_target = best;
				_loseInterestTimer = TickTimer.CreateFromSeconds(Runner, _profile.LoseInterestSeconds);
			}
			else if (_loseInterestTimer.ExpiredOrNotRunning(Runner))
			{
				_target = null;
			}
		}

		/// <summary>
		/// How visible/audible a player is to this monster right now, 0 = undetected. Combines distance, the
		/// vision cone, line of sight, and — the important one — how exposed the ground under them is.
		/// </summary>
		private float Detectability(Player player)
		{
			Vector3 toTarget = player.transform.position - transform.position;
			float distance = toTarget.magnitude;
			if (distance < 0.01f) return 1f;

			float exposure = ExposureAt(player.transform.position);
			float sight = _profile.SightRange * exposure;

			// Sound first — it ignores the vision cone and walls, which is what makes Listeners frightening and
			// what punishes sprinting past a corner.
			float noise = NoiseRadiusOf(player);
			if (noise > 0f && distance <= Mathf.Min(noise, _profile.HearingRange + noise * 0.5f))
				return 1f - distance / Mathf.Max(0.01f, _profile.HearingRange + noise);

			if (sight <= 0f || distance > sight) return 0f;

			float angle = Vector3.Angle(transform.forward, toTarget.normalized);
			if (angle > _profile.SightAngle * 0.5f) return 0f;

			// Only now pay for a raycast.
			var origin = transform.position + Vector3.up * 1.6f;
			var targetPoint = player.transform.position + Vector3.up * 1.0f;
			if (Physics.Linecast(origin, targetPoint, out _, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
				return 0f;

			return 1f - distance / sight;
		}

		/// <summary>
		/// Cover multiplier for a position: wide open on a road or an empty lot, hidden inside a building.
		/// Reading this from the layout rather than from geometry is what lets "stay off the street" be a rule
		/// players can learn and rely on, instead of an emergent accident of where the walls happen to be.
		/// </summary>
		private float ExposureAt(Vector3 position)
		{
			if (_layout == null) return 1f;

			var lot = _layout.LotAt(position);
			if (lot == null) return _profile.OpenGroundSightMultiplier;        // on a road

			return lot.Use switch
			{
				LotUse.Building                       => _profile.CoveredSightMultiplier,
				LotUse.Yard or LotUse.ConstructionSite => 1f,                  // clutter, partial cover
				_                                      => _profile.OpenGroundSightMultiplier,
			};
		}

		/// <summary>How far a player's own movement carries. Sprinting is loud; walking is nearly silent.</summary>
		private static float NoiseRadiusOf(Player player)
		{
			var velocity = player.KCC != null ? player.KCC.RealVelocity : Vector3.zero;
			float speed = new Vector2(velocity.x, velocity.z).magnitude;

			if (speed < 1.5f) return 0f;    // crouched / creeping
			if (speed < 4f) return 6f;      // walking
			return 18f;                     // sprinting
		}

		// =========================================================================
		// Behaviour
		// =========================================================================

		private void UpdateState()
		{
			if (_target != null)
			{
				if (State != MonsterState.Hunt)
				{
					State = MonsterState.Hunt;
					_repathTimer = default; // an expired-or-not-running timer forces an immediate repath
				}

				if (_repathTimer.ExpiredOrNotRunning(Runner))
				{
					_repathTimer = TickTimer.CreateFromSeconds(Runner, RepathInterval);
					SetDestination(_target.transform.position);
				}
				return;
			}

			// Nothing detected. Finish the current path, then pick somewhere new to be.
			if (_pathCursor < _path.Count) return;

			State = State == MonsterState.Search ? MonsterState.Search : MonsterState.Patrol;
			Vector3 anchor = State == MonsterState.Search ? Destination : _homePosition;
			WanderNear(anchor, _profile.PatrolRadius);
		}

		private void WanderNear(Vector3 anchor, float radius)
		{
			if (_graph == null) return;
			int node = _graph.RandomNodeNear(anchor, radius, ref _rng);
			if (node < 0) return;
			SetDestination(_graph.Position(node));
		}

		/// <summary>
		/// Routes to a point anywhere in the city over the baked NavMesh — which covers streets, interiors,
		/// stair ramps and rooftops, because the whole static city is baked in the editor. Closed barriers carve
		/// the mesh (see <c>Barrier</c>), so a monster's route honestly respects doors it cannot open: a hunt
		/// that ends behind a sealed door becomes a monster pacing outside it. Falls back to the street graph
		/// for scenes with no baked NavMesh.
		/// </summary>
		private void SetDestination(Vector3 worldPosition)
		{
			Destination = worldPosition;
			_path.Clear();
			_pathCursor = 0;

			if (TryNavMeshPath(worldPosition)) return;

			if (_graph != null)
			{
				int from = _graph.NearestNode(transform.position);
				int to = _graph.NearestNode(worldPosition);
				if (_graph.TryFindPath(from, to, _streetScratch))
					_path.AddRange(_streetScratch);
			}

			// Finish the last few metres directly — or head straight there when nothing could path at all.
			_path.Add(worldPosition);
		}

		private bool TryNavMeshPath(Vector3 target)
		{
			_navPath ??= new NavMeshPath();

			if (!NavMesh.SamplePosition(transform.position, out NavMeshHit fromHit, 4f, NavMesh.AllAreas)) return false;
			if (!NavMesh.SamplePosition(target, out NavMeshHit toHit, 6f, NavMesh.AllAreas)) return false;
			if (!NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, _navPath)) return false;
			if (_navPath.status == NavMeshPathStatus.PathInvalid || _navPath.corners.Length == 0) return false;

			var corners = _navPath.corners;
			for (int i = 0; i < corners.Length; i++)
				_path.Add(corners[i]);

			// A partial path already ends at the closest reachable point (e.g. outside a carved barrier) —
			// appending the true target would send the monster face-first into the door.
			if (_navPath.status == NavMeshPathStatus.PathComplete)
				_path.Add(target);

			return true;
		}

		/// <summary>Host-only: send this monster to sweep an area. Used when an objective terminal goes live.</summary>
		public void HostSearchArea(Vector3 center)
		{
			if (!HasStateAuthority) return;
			State = MonsterState.Search;
			SetDestination(center);
		}

		/// <summary>Host-only: react to a noise the world made (a shutter pried, glass broken, a shot).</summary>
		public void HostHearNoise(Vector3 position, float loudness)
		{
			if (!HasStateAuthority || _profile == null) return;
			if (State == MonsterState.Hunt) return;
			if (Vector3.Distance(position, transform.position) > loudness) return;

			State = MonsterState.Investigate;
			SetDestination(position);
		}

		// =========================================================================
		// Motion
		// =========================================================================

		private void Move(float deltaTime)
		{
			if (_pathCursor >= _path.Count) return;

			Vector3 waypoint = _path[_pathCursor];
			Vector3 flat = waypoint - transform.position;
			flat.y = 0f;

			if (flat.sqrMagnitude <= WaypointTolerance * WaypointTolerance)
			{
				_pathCursor++;
				return;
			}

			Vector3 direction = flat.normalized;
			float speed = State == MonsterState.Hunt ? _profile.HuntSpeed : _profile.PatrolSpeed;

			transform.rotation = Quaternion.RotateTowards(
				transform.rotation, Quaternion.LookRotation(direction, Vector3.up), TurnSpeed * deltaTime);

			Vector3 step = direction * (speed * deltaTime);
			if (_controller != null)
			{
				// Gravity so it follows the ground and stairs rather than floating off a kerb.
				step += Vector3.down * 9.81f * deltaTime;
				_controller.Move(step);
			}
			else
			{
				transform.position += step;
			}
		}

		private void TryKill()
		{
			if (_target == null || _target.Health == null || !_target.Health.IsAlive) return;
			if (Vector3.Distance(transform.position, _target.transform.position) > _profile.KillRange) return;

			// Contact is fatal. There is no damage curve to tune here on purpose — a monster you can trade hits
			// with is a monster you fight, and fighting is not what this game is.
			_target.Health.AuthorityKill();
			_target = null;
			State = MonsterState.Investigate;
		}
	}
}
