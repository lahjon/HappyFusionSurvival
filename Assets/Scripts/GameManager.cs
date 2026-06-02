using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Starter.Common;
using Starter.Common.Crafting;
using Starter.Common.Inventory;
using Starter.Common.Quests;

namespace Starter.Shooter
{
	/// <summary>
	/// Handles player connections (spawning of Player instances).
	/// </summary>
	public enum SleepPhase : byte
	{
		None      = 0,
		FadingIn  = 1,
		FadingOut = 2,
	}

	public sealed class GameManager : NetworkBehaviour, IPlayerJoined, IPlayerLeft
	{
		public Player PlayerPrefab;
		public ItemDatabase ItemDatabase;
		public RecipeDatabase RecipeDatabase;
		public QuestDatabase QuestDatabase;

		[Header("World Generation")]
		[Tooltip("Deterministic seed for procedural loot rolls. 0 = pick a random seed per session.")]
		[SerializeField] private int _seedOverride;

		[Header("Sleep Skip")]
		[Tooltip("Seconds the screen fades to black before the time jump to morning.")]
		[Min(0f)] public float SleepFadeInDuration = 1f;
		[Tooltip("Seconds the screen fades back from black after the time jump.")]
		[Min(0f)] public float SleepFadeOutDuration = 1f;

		[Networked]
		public int NetworkedWorldSeed { get; set; }

		/// <summary>Current phase of the "everyone asleep -> morning" fade sequence. Driven by state authority; read by <see cref="SleepFadeOverlay"/> on every client to draw the fade.</summary>
		[Networked]
		public SleepPhase CurrentSleepPhase { get; private set; }

		/// <summary>TickTimer for the current <see cref="CurrentSleepPhase"/>. Used by clients to compute fade alpha each frame.</summary>
		[Networked]
		public TickTimer SleepPhaseTimer { get; private set; }

		public Player LocalPlayer { get; private set; }

		/// <summary>State-authority-only roster of spawned players (kept in join order). Read by <see cref="MatchManager"/>
		/// and <see cref="TeamManager"/> for win-condition scans and team assignment. Always empty / wrong on remotes.</summary>
		public IReadOnlyList<Player> Players => _players;

		private List<Player> _players = new(32);
		private SpawnPoint[] _spawnPoints;
		// Tracks match phase on the host to fire the per-round reset exactly on the transition into Day.
		private MatchPhase _lastSeenPhase = MatchPhase.Lobby;

		private void Awake()
		{
			Debug.Log($"[SPAWNDBG] GameManager.Awake on '{name}' (NetworkObject present={(GetComponent<NetworkObject>() != null)})");
			// Bind on every client (authority + remotes) so item lookups work without Resources.
			if (ItemDatabase != null)
				ItemDatabase.Bind();
			if (RecipeDatabase != null)
				RecipeDatabase.Bind();
			if (QuestDatabase != null)
				QuestDatabase.Bind();

			// Awake fires before any Fusion Spawned() so scene-placed PickupableItem /
			// LootContainer rolls see a valid seed during their own Spawned().
			// Host & client both run this — the host's value is the authoritative one;
			// the client's local pick is overwritten in Spawned() from the [Networked] mirror.
			WorldGen.Seed = _seedOverride != 0 ? _seedOverride : unchecked((int)System.DateTime.UtcNow.Ticks);
		}

		public override void Spawned()
		{
			Debug.Log($"[SPAWNDBG] GameManager.Spawned hasStateAuth={HasStateAuthority} object={Object?.Id}");
			_spawnPoints = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude);

			if (HasStateAuthority)
				NetworkedWorldSeed = WorldGen.Seed;
			else
				WorldGen.Seed = NetworkedWorldSeed;
		}

		public override void FixedUpdateNetwork()
		{
			for (int i = 0; i < _players.Count; i++)
			{
				var player = _players[i];
				if (player == null) continue;

				// Fall-through-the-world kill. With per-death respawn removed (Purge "dead is dead"),
				// this is now a permanent elimination, same as a combat death.
				if (player.Health.IsAlive && player.KCC.Position.y < -15f)
				{
					player.Health.TakeHit(1000);
				}
			}

			if (HasStateAuthority)
			{
				// A new round begins when the match enters Day — revive + reposition everyone. This is the
				// ONLY place players (and bots) come back now that per-death respawn is gone. Polled here in
				// FixedUpdateNetwork so the [Networked] mutations inside Respawn stay on the simulation path
				// (rather than firing from an OnChangedRender event).
				var phase = MatchManager.Instance != null ? MatchManager.Instance.Phase : MatchPhase.Lobby;
				if (phase != _lastSeenPhase)
				{
					if (phase == MatchPhase.Day)
						ResetPlayersForNewRound();
					_lastSeenPhase = phase;
				}

				TickSleepSkip();
			}
		}

		// Revive + reposition every roster member at the start of a round (Lobby/MatchOver → Day). Teams are
		// already assigned by MatchManager.BeginMatch before the phase flips, so GetSpawnPositionForPlayer
		// places each player in their team zone.
		private void ResetPlayersForNewRound()
		{
			for (int i = 0; i < _players.Count; i++)
			{
				var player = _players[i];
				if (player == null || player.Object == null) continue;
				player.Respawn(GetSpawnPositionForPlayer(player.Owner));
			}
		}

		// Host-side state machine that watches for "all alive players asleep" and runs the
		// fade-to-morning sequence. Clients read CurrentSleepPhase + SleepPhaseTimer to drive
		// the local fade overlay (see SleepFadeOverlay).
		private void TickSleepSkip()
		{
			switch (CurrentSleepPhase)
			{
				case SleepPhase.None:
					if (AllAlivePlayersSleeping())
					{
						CurrentSleepPhase = SleepPhase.FadingIn;
						SleepPhaseTimer = TickTimer.CreateFromSeconds(Runner, SleepFadeInDuration);
					}
					break;

				case SleepPhase.FadingIn:
					if (SleepPhaseTimer.ExpiredOrNotRunning(Runner))
					{
						// Screen is fully black on every client — safe to teleport time and wake sleepers.
						if (TimeManager.Instance != null)
						{
							TimeManager.Instance.AdvanceToNextMorning();
						}
						ForceWakeAllSleepers();

						CurrentSleepPhase = SleepPhase.FadingOut;
						SleepPhaseTimer = TickTimer.CreateFromSeconds(Runner, SleepFadeOutDuration);
					}
					break;

				case SleepPhase.FadingOut:
					if (SleepPhaseTimer.ExpiredOrNotRunning(Runner))
					{
						CurrentSleepPhase = SleepPhase.None;
						SleepPhaseTimer = default;
					}
					break;
			}
		}

		private bool AllAlivePlayersSleeping()
		{
			int alive = 0;
			int sleeping = 0;
			for (int i = 0; i < _players.Count; i++)
			{
				var p = _players[i];
				if (p == null || p.Health == null || p.Health.IsAlive == false) continue;
				alive++;
				if (p.IsSleeping) sleeping++;
			}
			// Need at least one alive player, and every alive player must be asleep.
			return alive > 0 && alive == sleeping;
		}

		private void ForceWakeAllSleepers()
		{
			// Walk live beds rather than tracking a registry — the sequence runs at most twice
			// per night, and the scene typically has a handful of beds at most.
			var beds = FindObjectsByType<Bed>(FindObjectsInactive.Exclude);
			for (int i = 0; i < beds.Length; i++)
			{
				if (beds[i] != null) beds[i].HostReleaseOccupant();
			}
		}

		/// <summary>Active duration (seconds) of the current phase, used by <see cref="SleepFadeOverlay"/> to compute fade alpha.</summary>
		public float CurrentSleepPhaseDuration => CurrentSleepPhase switch
		{
			SleepPhase.FadingIn  => SleepFadeInDuration,
			SleepPhase.FadingOut => SleepFadeOutDuration,
			_ => 0f,
		};

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			// Clear the reference because UI can try to access it even after despawn
			LocalPlayer = null;
		}

		public override void Render()
		{
			// Prepare LocalPlayer property that can be accessed from UI
			if (LocalPlayer == null || LocalPlayer.Object == null || LocalPlayer.Object.IsValid == false)
			{
				var playerObject = Runner.GetPlayerObject(Runner.LocalPlayer);
				LocalPlayer = playerObject != null ? playerObject.GetComponent<Player>() : null;
			}
		}

		public void PlayerJoined(PlayerRef playerRef)
		{
			Debug.Log($"[SPAWNDBG] PlayerJoined ref={playerRef} hasStateAuth={HasStateAuthority} prefab={(PlayerPrefab == null ? "NULL" : PlayerPrefab.name)} spawnPoints={(_spawnPoints == null ? -1 : _spawnPoints.Length)}");

			if (HasStateAuthority == false)
				return;

			var pos = GetSpawnPositionForPlayer(playerRef);
			Debug.Log($"[SPAWNDBG] spawning at {pos}");
			var player = Runner.Spawn(PlayerPrefab, pos, Quaternion.identity, playerRef);
			Debug.Log($"[SPAWNDBG] Runner.Spawn returned {(player == null ? "NULL" : player.name)}");
			Runner.SetPlayerObject(playerRef, player.Object);

			// This list is state authority only,
			// so it is valid to have this list non-networked
			_players.Add(player);

			// Late-join into an active match: TeamManager places Day joiners on the smallest team; ignored
			// during Lobby (assigned at BeginMatch) and during Night/MatchOver (spectator until next round).
			TeamManager.Instance?.RegisterLateJoin(playerRef);
		}

		public void PlayerLeft(PlayerRef playerRef)
		{
			if (HasStateAuthority == false)
				return;

			int index = _players.FindIndex(t => t.Object.InputAuthority == playerRef);
			if (index >= 0)
			{
				Runner.Despawn(_players[index].Object);
				_players.RemoveAt(index);
			}
		}

		// =========================================================================
		// AI bots (host-authoritative; driven from the add_bot debug command)
		// =========================================================================

		/// <summary>Synthetic <c>PlayerId</c> base for bot <see cref="Player.Owner"/> refs. Kept far above
		/// <see cref="TeamManager.MaxPlayers"/> so a bot's Owner can never collide with a real connection's PlayerRef.</summary>
		public const int BotRefBase = 1000;

		private int _nextBotIndex;

		/// <summary>Host-only: spawn <paramref name="count"/> AI bots as Player objects with synthetic Owner refs. They
		/// join <see cref="Players"/> (so the win scan counts them) and are teamed onto bot-only (enemy) teams when a
		/// round is already underway, or at the next <see cref="MatchManager.BeginMatch"/> when added in the lobby.
		/// Returns how many actually spawned.</summary>
		public int AddBots(int count)
		{
			if (HasStateAuthority == false || count <= 0) return 0;

			var mm = MatchManager.Instance;
			bool roundActive = mm != null && mm.Phase != MatchPhase.Lobby && mm.Phase != MatchPhase.MatchOver;

			int spawned = 0;
			for (int i = 0; i < count; i++)
			{
				var owner = PlayerRef.FromIndex(BotRefBase + _nextBotIndex);
				_nextBotIndex++;

				var bot = Runner.Spawn(PlayerPrefab, GetSpawnPosition(), Quaternion.identity, null,
					(runner, obj) =>
					{
						var player = obj.GetComponent<Player>();
						if (player != null) player.ConfigureAsBot(owner);
					});
				if (bot == null) continue;

				_players.Add(bot);
				if (roundActive)
					TeamManager.Instance?.RegisterBotJoin(owner);

				spawned++;
			}
			return spawned;
		}

		/// <summary>Host-only: despawn up to <paramref name="count"/> bots, most-recently-added first. Returns the count removed.</summary>
		public int RemoveBots(int count)
		{
			if (HasStateAuthority == false || count <= 0) return 0;

			int removed = 0;
			for (int i = _players.Count - 1; i >= 0 && removed < count; i--)
			{
				var p = _players[i];
				if (p == null || p.IsBot == false) continue;

				if (p.Object != null) Runner.Despawn(p.Object);
				_players.RemoveAt(i);
				removed++;
			}
			return removed;
		}

		/// <summary>Host-only: despawn every bot.</summary>
		public int ClearBots() => RemoveBots(int.MaxValue);

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_DebugAddBots(int count)
		{
			if (HasStateAuthority) AddBots(count);
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_DebugRemoveBots(int count)
		{
			if (HasStateAuthority) RemoveBots(count);
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_DebugClearBots()
		{
			if (HasStateAuthority) ClearBots();
		}

		/// <summary>Spawn position inside the player's team zone when one is assigned (Day start / Night respawn),
		/// otherwise a random spawn (Lobby joins with no team yet).</summary>
		private Vector3 GetSpawnPositionForPlayer(PlayerRef player)
		{
			int zoneId = ZoneManager.Instance != null ? ZoneManager.Instance.ZoneIdOfPlayer(player) : -1;
			return GetSpawnPosition(zoneId);
		}

		private Vector3 GetSpawnPosition() => GetSpawnPosition(-1);

		private Vector3 GetSpawnPosition(int zoneId)
		{
			var spawnPoint = PickSpawnPoint(zoneId);
			var randomPositionOffset = Random.insideUnitCircle * spawnPoint.Radius;
			Vector3 position = spawnPoint.transform.position + new Vector3(randomPositionOffset.x, 0f, randomPositionOffset.y);

			// Snap to the floor: cast down from a few meters above so a misplaced spawn
			// point (e.g. below the ground plane) can't drop the player past the y<-15
			// kill plane checked in FixedUpdateNetwork.
			Vector3 castOrigin = position + Vector3.up * 5f;
			if (Physics.Raycast(castOrigin, Vector3.down, out RaycastHit hit, 25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
			{
				position.y = hit.point.y + 0.5f;
			}
			return position;
		}

		// Random spawn point tagged with the requested zone; falls back to any spawn point when zoneId is
		// negative or no spawn carries that tag. Reservoir-style pick avoids allocating a filtered list.
		private SpawnPoint PickSpawnPoint(int zoneId)
		{
			if (zoneId >= 0 && _spawnPoints != null)
			{
				int count = 0;
				for (int i = 0; i < _spawnPoints.Length; i++)
					if (_spawnPoints[i] != null && _spawnPoints[i].ZoneId == zoneId) count++;

				if (count > 0)
				{
					int pick = Random.Range(0, count);
					for (int i = 0; i < _spawnPoints.Length; i++)
					{
						if (_spawnPoints[i] == null || _spawnPoints[i].ZoneId != zoneId) continue;
						if (pick-- == 0) return _spawnPoints[i];
					}
				}
			}
			return _spawnPoints[Random.Range(0, _spawnPoints.Length)];
		}
	}
}
