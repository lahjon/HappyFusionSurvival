using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Owns the team-size selection and per-<see cref="PlayerRef"/> team assignment for a round. Driven by
	/// <see cref="MatchManager"/>: <see cref="AssignTeams"/> runs at Lobby→Day, <see cref="ClearAssignments"/>
	/// at MatchOver→Lobby. Damage gating in <c>Health.ApplyDamage</c> reads <see cref="SameTeam"/>.
	///
	/// Scene-placed sibling of <see cref="MatchManager"/>.
	/// </summary>
	public sealed class TeamManager : NetworkBehaviour
	{
		public static TeamManager Instance { get; private set; }

		public const int MaxPlayers = 18;
		public const int MinTeamSize = 1;
		public const int MaxTeamSize = 3;

		[Header("Defaults")]
		[Tooltip("Team size used if the host never sets one before BeginMatch (1=Solo, 2=Duo, 3=Trio).")]
		[Range(MinTeamSize, MaxTeamSize)] public int DefaultTeamSize = 2;

		// =========================================================================
		// Networked state
		// =========================================================================

		/// <summary>1=Solo, 2=Duo, 3=Trio. Host sets via <see cref="SetTeamSize"/> while in Lobby.</summary>
		[Networked] public int TeamSize { get; private set; }

		/// <summary>Maps PlayerRef → team id (0..MaxPlayers-1). State authority only writes. Missing entries = unassigned.</summary>
		[Networked, Capacity(MaxPlayers)]
		public NetworkDictionary<PlayerRef, int> TeamByPlayer => default;

		/// <summary>Kill count per team id during Night. Indexed by team id. Used for the timer-expiry tiebreaker.</summary>
		[Networked, Capacity(MaxPlayers)]
		public NetworkArray<int> TeamKills => default;

		// =========================================================================
		// Lifecycle
		// =========================================================================

		private GameManager _gameManager;

		public override void Spawned()
		{
			Instance = this;
			_gameManager = FindAnyObjectByType<GameManager>();

			if (HasStateAuthority && TeamSize == 0)
				TeamSize = DefaultTeamSize;
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (Instance == this) Instance = null;
		}

		// =========================================================================
		// Host-driven mutations
		// =========================================================================

		/// <summary>Host picks team size while in Lobby. Clamped to [1, 3].</summary>
		public void SetTeamSize(int size)
		{
			if (HasStateAuthority == false) return;
			if (MatchManager.Instance != null && MatchManager.Instance.Phase != MatchPhase.Lobby) return;
			TeamSize = Mathf.Clamp(size, MinTeamSize, MaxTeamSize);
		}

		/// <summary>Assigns every currently-spawned player to a team. Deterministic: sorted by PlayerRef.PlayerId,
		/// chunked into groups of <see cref="TeamSize"/>. Called by <see cref="MatchManager.BeginMatch"/>.</summary>
		public void AssignTeams()
		{
			if (HasStateAuthority == false) return;

			TeamByPlayer.Clear();
			for (int i = 0; i < TeamKills.Length; i++) TeamKills.Set(i, 0);

			int size = Mathf.Max(1, TeamSize);
			var roster = CollectRoster();
			roster.Sort((a, b) => a.PlayerId.CompareTo(b.PlayerId));

			for (int i = 0; i < roster.Count; i++)
			{
				int teamId = i / size;
				TeamByPlayer.Set(roster[i], teamId);
			}
		}

		/// <summary>Wipe team data — called on MatchOver→Lobby.</summary>
		public void ClearAssignments()
		{
			if (HasStateAuthority == false) return;
			TeamByPlayer.Clear();
			for (int i = 0; i < TeamKills.Length; i++) TeamKills.Set(i, 0);
		}

		/// <summary>Adds a late-joining player to the smallest existing team during Day; spectator (no team) otherwise.</summary>
		public void RegisterLateJoin(PlayerRef player)
		{
			if (HasStateAuthority == false) return;
			if (TeamByPlayer.ContainsKey(player)) return;

			var phase = MatchManager.Instance != null ? MatchManager.Instance.Phase : MatchPhase.Lobby;
			if (phase != MatchPhase.Day) return; // Night/DuskWarning/MatchOver → spectator until next round.

			int size = Mathf.Max(1, TeamSize);

			// Count occupants per existing team; bucket size is capped by team size.
			System.Span<int> counts = stackalloc int[MaxPlayers];
			int maxTeamId = -1;
			foreach (var kvp in TeamByPlayer)
			{
				if (kvp.Value < 0 || kvp.Value >= MaxPlayers) continue;
				counts[kvp.Value]++;
				if (kvp.Value > maxTeamId) maxTeamId = kvp.Value;
			}

			// Find the smallest team with room.
			int bestTeam = -1;
			int bestCount = int.MaxValue;
			for (int t = 0; t <= maxTeamId; t++)
			{
				if (counts[t] >= size) continue;
				if (counts[t] < bestCount) { bestCount = counts[t]; bestTeam = t; }
			}

			// All existing teams full → open a new one.
			if (bestTeam < 0) bestTeam = maxTeamId + 1;

			TeamByPlayer.Set(player, bestTeam);
		}

		/// <summary>Bump kill count for the attacker's team. No-op if attacker unassigned. Call from <c>Health.ApplyDamage</c>
		/// on killing blow, state-authority only.</summary>
		public void RegisterKill(PlayerRef attacker)
		{
			if (HasStateAuthority == false) return;
			if (TeamByPlayer.TryGet(attacker, out int teamId) == false) return;
			if (teamId < 0 || teamId >= TeamKills.Length) return;
			TeamKills.Set(teamId, TeamKills[teamId] + 1);
		}

		// =========================================================================
		// Read helpers (called from anywhere)
		// =========================================================================

		public int TeamOf(PlayerRef player)
		{
			return TeamByPlayer.TryGet(player, out int teamId) ? teamId : -1;
		}

		public bool SameTeam(PlayerRef a, PlayerRef b)
		{
			if (a == PlayerRef.None || b == PlayerRef.None) return false;
			if (a == b) return true;
			int ta = TeamOf(a);
			if (ta < 0) return false;
			int tb = TeamOf(b);
			return ta == tb;
		}

		/// <summary>Counts distinct teams with at least one living player. Sets <paramref name="lastTeamId"/> to that
		/// team's id when exactly one remains (otherwise -1). Used by <see cref="MatchManager"/> for win detection.</summary>
		public int CountLivingTeams(out int lastTeamId)
		{
			lastTeamId = -1;

			// Bitmask of teams with at least one living member.
			System.Span<bool> living = stackalloc bool[MaxPlayers];
			int distinct = 0;

			var players = _gameManager != null ? _gameManager.Players : null;
			if (players == null) return 0;

			for (int i = 0; i < players.Count; i++)
			{
				var p = players[i];
				if (p == null || p.Health == null || p.Health.IsAlive == false) continue;

				int teamId = TeamOf(p.Object.InputAuthority);
				if (teamId < 0 || teamId >= MaxPlayers) continue;

				if (living[teamId] == false)
				{
					living[teamId] = true;
					distinct++;
					lastTeamId = teamId;
				}
			}

			if (distinct != 1) lastTeamId = -1;
			return distinct;
		}

		/// <summary>Resolves a winner when the Night timer expires with multiple teams still alive. Picks team with
		/// most kills; ties broken by lowest team id (deterministic). Returns -1 if no team has any kills.</summary>
		public int TiebreakWinningTeam()
		{
			int bestTeam = -1;
			int bestKills = -1;
			for (int t = 0; t < TeamKills.Length; t++)
			{
				int k = TeamKills[t];
				if (k > bestKills) { bestKills = k; bestTeam = t; }
			}
			return bestKills > 0 ? bestTeam : -1;
		}

		// =========================================================================
		// Internals
		// =========================================================================

		private System.Collections.Generic.List<PlayerRef> CollectRoster()
		{
			var list = new System.Collections.Generic.List<PlayerRef>(MaxPlayers);
			if (_gameManager == null) return list;
			var players = _gameManager.Players;
			for (int i = 0; i < players.Count; i++)
			{
				var p = players[i];
				if (p == null || p.Object == null) continue;
				list.Add(p.Object.InputAuthority);
			}
			return list;
		}
	}
}
