using System.Collections.Generic;
using Fusion;
using Starter.City;
using Starter.Run;
using UnityEngine;

namespace Starter.Monsters
{
	/// <summary>
	/// Populates the city and escalates it.
	///
	/// <para><b>Few, but everywhere.</b> A square kilometre with a dozen monsters in it is mostly empty — which
	/// is the point. Density is low enough that players can cross a district and meet nothing, and that
	/// uncertainty is what makes every street crossing a decision rather than a routine. Filling the city with
	/// threats would make it predictable in the worst way: uniformly dangerous is the same as uniformly safe.</para>
	///
	/// <para><b>Activating a terminal is the escalation trigger.</b> Bringing one online tells the city exactly
	/// where the players are: everything nearby starts sweeping that area, and reinforcements arrive. The four
	/// objectives therefore get progressively harder to reach in whatever order they are taken, without any
	/// difficulty curve being authored — the players build it themselves.</para>
	///
	/// <para>Host-authoritative throughout. Monsters are the only networked moving objects the run spawns.</para>
	/// </summary>
	public sealed class MonsterDirector : NetworkBehaviour
	{
		public static MonsterDirector Instance { get; private set; }

		[Header("Prefab")]
		[SerializeField] private Monster _monsterPrefab;

		[Header("Population")]
		[Tooltip("Monsters in the city at the start of a run, before anything is activated.")]
		[Min(0)] public int StartingCount = 12;

		[Tooltip("Extra monsters drawn to the area each time an objective terminal is activated.")]
		[Min(0)] public int EscalationSpawns = 3;

		[Tooltip("Radius around a newly activated terminal that existing monsters are pulled into searching.")]
		[Min(20f)] public float EscalationRadius = 260f;

		[Tooltip("How far from the players' start position monsters are kept at spawn. The first minute of a run " +
			"should be quiet enough to get oriented.")]
		[Min(0f)] public float StartSafeRadius = 140f;

		[Header("Profiles")]
		[Tooltip("One profile per MonsterType. Missing entries fall back to the first.")]
		[SerializeField] private MonsterProfile[] _profiles;

		private readonly List<Monster> _monsters = new List<Monster>(32);
		private CityNavGraph _graph;
		private RunRng _rng;
		private bool _populated;

		// =====================================================================

		public override void Spawned()
		{
			Instance = this;
			RunState.ObjectiveActivated += OnObjectiveActivated;
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (Instance == this) Instance = null;
			RunState.ObjectiveActivated -= OnObjectiveActivated;
		}

		public override void FixedUpdateNetwork()
		{
			if (_populated || !HasStateAuthority) return;

			var director = RunDirector.Instance;
			if (director == null || !director.IsReady) return;

			Populate(director);
			_populated = true;
		}

		// =====================================================================
		// Population
		// =====================================================================

		private void Populate(RunDirector director)
		{
			if (_monsterPrefab == null)
			{
				Debug.LogWarning("[MonsterDirector] No monster prefab assigned — the city will be empty.");
				return;
			}

			_graph = CityNavGraph.Build(director.Layout);
			_rng = RunRng.ForRun(director.Plan != null ? director.Plan.Seed : 0, RngStream.Monsters);

			Vector3 start = director.Plan != null ? director.Plan.StartPosition : director.Layout.Bounds.center;

			for (int i = 0; i < StartingCount; i++)
			{
				if (!TryPickSpawn(director.Layout, start, StartSafeRadius, out Vector3 position)) continue;
				SpawnMonster(director, PickType(director.Layout, position), position, i);
			}

			Debug.Log($"[MonsterDirector] Populated {_monsters.Count} monsters across {director.Layout.Districts.Count} districts.");
		}

		/// <summary>
		/// Picks somewhere on the street network at least <paramref name="safeRadius"/> from the team's start.
		/// Gives up after a bounded number of tries rather than looping — a cramped layout should cost a monster,
		/// not a hang.
		/// </summary>
		private bool TryPickSpawn(CityLayout layout, Vector3 start, float safeRadius, out Vector3 position)
		{
			position = default;
			if (_graph == null || _graph.NodeCount == 0) return false;

			for (int attempt = 0; attempt < 24; attempt++)
			{
				int node = _rng.Index(_graph.NodeCount);
				Vector3 candidate = _graph.Position(node);
				if (Vector3.Distance(candidate, start) < safeRadius) continue;

				position = candidate;
				return true;
			}
			return false;
		}

		/// <summary>Type is chosen by where it lives: Sentinels claim the wide open districts, Listeners haunt the
		/// dense ones where players are forced to move fast through tight spaces.</summary>
		private MonsterType PickType(CityLayout layout, Vector3 position)
		{
			var lot = layout.LotAt(position);
			var district = lot != null ? layout.DistrictOfLot(lot) : null;
			var type = district != null ? district.Type : DistrictType.Commercial;

			float roll = _rng.NextFloat();
			return type switch
			{
				DistrictType.Downtown or DistrictType.Civic =>
					roll < 0.5f ? MonsterType.Sentinel : MonsterType.Stalker,
				DistrictType.Industrial =>
					roll < 0.55f ? MonsterType.Listener : MonsterType.Sentinel,
				DistrictType.Commercial or DistrictType.Residential =>
					roll < 0.45f ? MonsterType.Listener : MonsterType.Stalker,
				_ => MonsterType.Stalker,
			};
		}

		private void SpawnMonster(RunDirector director, MonsterType type, Vector3 position, int index)
		{
			var spawned = Runner.Spawn(_monsterPrefab, position, Quaternion.identity);
			if (spawned == null) return;

			spawned.HostConfigure(type, ProfileFor(type), _graph, director.Layout,
				director.Plan != null ? director.Plan.Seed : 0, index);
			_monsters.Add(spawned);
		}

		private MonsterProfile ProfileFor(MonsterType type)
		{
			if (_profiles == null || _profiles.Length == 0) return new MonsterProfile { Type = type };

			for (int i = 0; i < _profiles.Length; i++)
				if (_profiles[i] != null && _profiles[i].Type == type) return _profiles[i];

			return _profiles[0];
		}

		// =====================================================================
		// Escalation
		// =====================================================================

		private void OnObjectiveActivated(int ordinal)
		{
			if (!HasStateAuthority) return;

			var director = RunDirector.Instance;
			var plan = director != null ? director.Plan : null;
			if (plan == null || ordinal < 0 || ordinal >= plan.ObjectiveSites.Count) return;

			var site = plan.Site(plan.ObjectiveSites[ordinal]);
			if (site == null) return;

			Vector3 focus = site.TargetPosition;

			// Everything already in the area converges on it.
			int pulled = 0;
			for (int i = _monsters.Count - 1; i >= 0; i--)
			{
				var monster = _monsters[i];
				if (monster == null) { _monsters.RemoveAt(i); continue; }
				if (Vector3.Distance(monster.transform.position, focus) > EscalationRadius) continue;

				monster.HostSearchArea(focus);
				pulled++;
			}

			// Plus reinforcements, placed far enough out that they arrive rather than appear.
			for (int i = 0; i < EscalationSpawns; i++)
			{
				if (!TryPickSpawn(director.Layout, focus, EscalationRadius * 0.5f, out Vector3 position)) continue;
				SpawnMonster(director, PickType(director.Layout, position), position, _monsters.Count);
			}

			Debug.Log($"[MonsterDirector] Objective {(char)('A' + ordinal)} online — {pulled} monsters converging, " +
				$"{EscalationSpawns} inbound.");
		}

		// =====================================================================
		// World events
		// =====================================================================

		/// <summary>
		/// Tell the city something made a noise. Host-only; call it from anything loud — prying a shutter,
		/// breaking a window, a gunshot, a dropped crate. <paramref name="loudness"/> is the radius in metres
		/// within which it can be heard.
		/// </summary>
		public static void ReportNoise(Vector3 position, float loudness)
		{
			var director = Instance;
			if (director == null || !director.HasStateAuthority) return;

			for (int i = director._monsters.Count - 1; i >= 0; i--)
			{
				var monster = director._monsters[i];
				if (monster == null) { director._monsters.RemoveAt(i); continue; }
				monster.HostHearNoise(position, loudness);
			}
		}
	}
}
