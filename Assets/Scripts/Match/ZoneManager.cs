using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Assigns each team a distinct staging <see cref="Zone"/> at match start and arms players' PvP weapons once
	/// they enter their team's zone during Night. Stops the Day→Night flip from becoming an instant cluster-kill:
	/// weapons can't fire at all until a player has returned to and stepped inside their team's zone (arm-once —
	/// leaving does not disarm). Fire gating itself lives in <see cref="Player"/>; this manager only owns the
	/// team→zone mapping and the per-player <c>PvpArmed</c> flag.
	///
	/// Scene-placed singleton — sits on the same NetworkObject as <see cref="MatchManager"/> / <see cref="TeamManager"/>.
	/// </summary>
	public sealed class ZoneManager : NetworkBehaviour
	{
		public static ZoneManager Instance { get; private set; }

		/// <summary>teamId → assigned <see cref="Zone.ZoneId"/>. -1 = unassigned. State authority writes at
		/// <see cref="AssignZones"/>; every peer reads it (so UI can show a player their zone).</summary>
		[Networked, Capacity(TeamManager.MaxPlayers)]
		public NetworkArray<int> TeamZone => default;

		private readonly Dictionary<int, Zone> _zonesById = new();
		private readonly List<Zone> _sortedZones = new();

		private TeamManager _teams;
		private GameManager _gameManager;
		private MatchPhase _prevPhase = MatchPhase.Lobby;

		// =========================================================================
		// Lifecycle
		// =========================================================================

		public override void Spawned()
		{
			Instance = this;

			_teams = FindAnyObjectByType<TeamManager>();
			_gameManager = FindAnyObjectByType<GameManager>();

			RebuildZoneCache();

			if (HasStateAuthority)
			{
				for (int i = 0; i < TeamZone.Length; i++)
					TeamZone.Set(i, -1);
			}
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (Instance == this) Instance = null;
		}

		// Auto-discover the zones and spawn points in the scene (mirrors how GameManager finds spawn points). Each
		// spawn point is tied to the zone whose footprint contains it — no manual tagging — so a team assigned a zone
		// can spawn at any of that zone's SpawnPoints.
		private void RebuildZoneCache()
		{
			_zonesById.Clear();
			_sortedZones.Clear();

			var zones = FindObjectsByType<Zone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
			for (int i = 0; i < zones.Length; i++)
			{
				var z = zones[i];
				if (z == null) continue;
				z.ClearSpawnPoints();
				if (_zonesById.ContainsKey(z.ZoneId))
				{
					Debug.LogWarning($"[ZoneManager] Duplicate Zone id {z.ZoneId} on '{z.name}' — ids must be unique. Ignoring duplicate.", z);
					continue;
				}
				_zonesById.Add(z.ZoneId, z);
				_sortedZones.Add(z);
			}
			_sortedZones.Sort((a, b) => a.ZoneId.CompareTo(b.ZoneId));

			AssignSpawnPointsToZones();
		}

		// Distribute every scene SpawnPoint to the first zone (by ascending ZoneId) whose footprint contains it.
		private void AssignSpawnPointsToZones()
		{
			var spawnPoints = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
			for (int i = 0; i < spawnPoints.Length; i++)
			{
				var sp = spawnPoints[i];
				if (sp == null) continue;

				bool claimed = false;
				for (int z = 0; z < _sortedZones.Count; z++)
				{
					if (_sortedZones[z].TryClaimSpawnPoint(sp)) { claimed = true; break; }
				}

				if (claimed == false)
					Debug.LogWarning($"[ZoneManager] SpawnPoint '{sp.name}' is not inside any Zone — it can only be used as a fallback spawn.", sp);
			}
		}

		// =========================================================================
		// Host-driven mutation
		// =========================================================================

		/// <summary>Maps every occupied team to a distinct zone. Deterministic: teams ordered by id, zones ordered by
		/// <see cref="Zone.ZoneId"/>. Called from <see cref="MatchManager.BeginMatch"/> right after team assignment.</summary>
		public void AssignZones()
		{
			if (HasStateAuthority == false) return;

			for (int i = 0; i < TeamZone.Length; i++)
				TeamZone.Set(i, -1);

			if (_sortedZones.Count == 0)
			{
				Debug.LogWarning("[ZoneManager] No Zone objects in scene — PvP arming can never enable at Night.");
				return;
			}
			if (_teams == null) return;

			int maxTeam = -1;
			foreach (var kvp in _teams.TeamByPlayer)
				if (kvp.Value > maxTeam) maxTeam = kvp.Value;

			int teamCount = maxTeam + 1;
			if (teamCount > _sortedZones.Count)
				Debug.LogWarning($"[ZoneManager] {teamCount} teams but only {_sortedZones.Count} zones — some teams will share a zone.");

			for (int t = 0; t <= maxTeam && t < TeamZone.Length; t++)
				TeamZone.Set(t, _sortedZones[t % _sortedZones.Count].ZoneId);
		}

		// =========================================================================
		// Arming scan (state authority)
		// =========================================================================

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false) return;

			var match = MatchManager.Instance;
			if (match == null) return;

			var phase = match.Phase;
			if (phase != _prevPhase)
			{
				// Each Night requires fresh arming; Lobby clears stale flags for the next round.
				if (phase == MatchPhase.Night || phase == MatchPhase.Lobby)
					SetAllArmed(false);
				_prevPhase = phase;
			}

			if (phase != MatchPhase.Night) return;
			ScanArming();
		}

		private void ScanArming()
		{
			var players = _gameManager != null ? _gameManager.Players : null;
			if (players == null || _teams == null) return;

			for (int i = 0; i < players.Count; i++)
			{
				var p = players[i];
				if (p == null || p.Object == null) continue;
				if (p.PvpArmed) continue; // arm-once
				if (p.Health == null || p.Health.IsAlive == false) continue;

				int team = _teams.TeamOf(p.Object.InputAuthority);
				if (team < 0 || team >= TeamZone.Length) continue; // spectator / unassigned

				int zoneId = TeamZone[team];
				if (zoneId < 0) continue;
				if (_zonesById.TryGetValue(zoneId, out var zone) == false || zone == null) continue;

				if (zone.Contains(p.KCC.Position))
					p.SetPvpArmed(true);
			}
		}

		private void SetAllArmed(bool armed)
		{
			var players = _gameManager != null ? _gameManager.Players : null;
			if (players == null) return;
			for (int i = 0; i < players.Count; i++)
			{
				var p = players[i];
				if (p != null) p.SetPvpArmed(armed);
			}
		}

		// =========================================================================
		// Read helpers (any peer — used by spawning and UI)
		// =========================================================================

		/// <summary>The <see cref="Zone.ZoneId"/> assigned to this player's team, or -1 if none.</summary>
		public int ZoneIdOfPlayer(PlayerRef player)
		{
			if (_teams == null) return -1;
			int team = _teams.TeamOf(player);
			if (team < 0 || team >= TeamZone.Length) return -1;
			return TeamZone[team];
		}

		/// <summary>The <see cref="Zone"/> assigned to this player's team, or null if none / not found.</summary>
		public Zone ZoneOfPlayer(PlayerRef player)
		{
			int id = ZoneIdOfPlayer(player);
			if (id < 0) return null;
			return _zonesById.TryGetValue(id, out var z) ? z : null;
		}

		/// <summary>The <see cref="Zone"/> with the given <see cref="Zone.ZoneId"/>, or null if none. Used by
		/// <see cref="GameManager"/> to pull a zone's spawn points when positioning a team.</summary>
		public Zone ZoneById(int zoneId)
		{
			if (zoneId < 0) return null;
			return _zonesById.TryGetValue(zoneId, out var z) ? z : null;
		}
	}
}
