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
		/// <summary>Scene singleton, set in <see cref="Spawned"/>. Lets hot-path callers (bot AI, match flow) reach the
		/// roster without a per-tick <c>FindAnyObjectByType</c> scan — mirrors <c>MatchManager.Instance</c> / <c>TeamManager.Instance</c>.</summary>
		public static GameManager Instance { get; private set; }

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
			Instance = this;
			Debug.Log($"[SPAWNDBG] GameManager.Spawned hasStateAuth={HasStateAuthority} object={Object?.Id}");
			_spawnPoints = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude);

			if (HasStateAuthority)
				NetworkedWorldSeed = WorldGen.Seed;
			else
				WorldGen.Seed = NetworkedWorldSeed;

			if (HasStateAuthority)
			{
				// Players were already connected in the lobby scene before this game scene loaded, so IPlayerJoined
				// does not fire for them here — spawn for every active player now. EnsurePlayerSpawned is idempotent,
				// so genuine late joins still arrive through PlayerJoined without double-spawning.
				foreach (var playerRef in Runner.ActivePlayers)
					EnsurePlayerSpawned(playerRef);
			}
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

				TickZoneDamage();
				TickSleepSkip();
			}
		}

		// Out-of-zone "Purge grid" damage. While the LightGrid's safe circle is collapsing during Night, any living
		// player standing in a blacked-out zone takes periodic environmental damage (no attacker — a "dead is dead"
		// elimination, exactly like the fall-through kill). Host-authority only; clients receive the Health change.
		private float _zoneDamageAccum;
		private void TickZoneDamage()
		{
			var grid = LightGrid.Instance;
			if (grid == null || MatchManager.Instance == null || MatchManager.Instance.Phase != MatchPhase.Night)
			{
				_zoneDamageAccum = 0f;
				return;
			}

			float interval = Mathf.Max(0.1f, grid.DamageInterval);
			_zoneDamageAccum += Runner.DeltaTime;
			if (_zoneDamageAccum < interval) return;
			_zoneDamageAccum -= interval;

			for (int i = 0; i < _players.Count; i++)
			{
				var player = _players[i];
				if (player == null || player.Health == null || player.Health.IsAlive == false) continue;
				if (grid.IsLethalAt(player.KCC.Position))
					player.Health.TakeHit(grid.OutOfZoneDamage);
			}
		}

		// Revive + reposition every roster member at the start of a round (Lobby/MatchOver → Day). Teams are
		// already assigned by MatchManager.BeginMatch before the phase flips, so GetSpawnPoseForPlayer
		// places each player in their team zone.
		private void ResetPlayersForNewRound()
		{
			for (int i = 0; i < _players.Count; i++)
			{
				var player = _players[i];
				if (player == null || player.Object == null) continue;
				GetSpawnPoseForPlayer(player.Owner, out var pos, out var rot);
				player.Respawn(pos, rot.eulerAngles.y);
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
			if (Instance == this) Instance = null;
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
			EnsurePlayerSpawned(playerRef);
		}

		/// <summary>Host-only, idempotent: spawn a <see cref="Player"/> for <paramref name="playerRef"/> if one does not
		/// already exist. Called both when the game scene first loads (for everyone already connected in the lobby) and
		/// from <see cref="PlayerJoined"/> for genuine late joins; the <see cref="NetworkRunner.GetPlayerObject"/> guard
		/// keeps the two paths from double-spawning.</summary>
		private void EnsurePlayerSpawned(PlayerRef playerRef)
		{
			if (HasStateAuthority == false)
				return;
			if (Runner.GetPlayerObject(playerRef) != null)
				return;

			GetSpawnPoseForPlayer(playerRef, out var pos, out var rot);
			var player = Runner.Spawn(PlayerPrefab, pos, rot, playerRef);
			Runner.SetPlayerObject(playerRef, player.Object);

			// Confine this player to their spawn area until the round begins (everyone loaded in). Late joiners into an
			// already-started round still get an anchor, but MatchManager.RoundStarted is already true so it never bites.
			player.InitSpawnConfinement(pos);

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

		/// <summary>Themed pool of bot display names. Picked from (without immediate repeats) in <see cref="NextBotName"/>
		/// so nameplates / team previews read like real opponents instead of "Bot 1". Host-only — the chosen name is
		/// written to the networked <see cref="Player.Nickname"/> in <see cref="Player.ConfigureAsBot"/>.</summary>
		private static readonly string[] BotNames =
		{
			"Mabel", "Cletus", "Pernilla", "Dorian", "Gunhild", "Bartholomew", "Saffron", "Ozzie",
			"Henrietta", "Murdoch", "Clementine", "Festus", "Wilhelmina", "Barnaby", "Prudence", "Gideon",
			"Esmeralda", "Thaddeus", "Bridget", "Cornelius", "Winnifred", "Horace", "Agnes", "Linus",
			"Drusilla", "Mortimer", "Beatrix", "Ignatius", "Lavinia", "Percival", "Tabitha", "Augustus",
		};

		/// <summary>Working copy of <see cref="BotNames"/> that we draw from without replacement, refilling (reshuffled)
		/// once exhausted, so a single match avoids duplicate bot names until the pool runs dry.</summary>
		private readonly List<string> _botNamePool = new List<string>();

		/// <summary>Host-only: next unique-ish random bot name. Falls back to "Bot N" only if the pool is somehow empty.</summary>
		private string NextBotName(int botIndex)
		{
			if (_botNamePool.Count == 0)
			{
				_botNamePool.AddRange(BotNames);
				// Fisher–Yates shuffle so the draw order differs each refill.
				for (int i = _botNamePool.Count - 1; i > 0; i--)
				{
					int j = Random.Range(0, i + 1);
					(_botNamePool[i], _botNamePool[j]) = (_botNamePool[j], _botNamePool[i]);
				}
			}

			if (_botNamePool.Count == 0)
				return $"Bot {botIndex + 1}";

			int last = _botNamePool.Count - 1;
			var name = _botNamePool[last];
			_botNamePool.RemoveAt(last);
			return name;
		}

		/// <summary>Host-only: spawn <paramref name="count"/> AI bots as Player objects with synthetic Owner refs. They
		/// join <see cref="Players"/> (so the win scan counts them) and are teamed onto bot-only (enemy) teams when a
		/// round is already underway, or at the next <see cref="MatchManager.BeginMatch"/> when added in the lobby.
		/// Returns how many actually spawned.</summary>
		public int AddBots(int count, BotDifficulty difficulty = BotDifficulty.Medium)
		{
			if (HasStateAuthority == false || count <= 0) return 0;

			var mm = MatchManager.Instance;
			bool roundActive = mm != null && mm.Phase != MatchPhase.Lobby && mm.Phase != MatchPhase.MatchOver;

			int spawned = 0;
			for (int i = 0; i < count; i++)
			{
				var owner = PlayerRef.FromIndex(BotRefBase + _nextBotIndex);
				var botName = NextBotName(_nextBotIndex);
				_nextBotIndex++;

				GetSpawnPose(out var botPos, out var botRot);
				var bot = Runner.Spawn(PlayerPrefab, botPos, botRot, null,
					(runner, obj) =>
					{
						var player = obj.GetComponent<Player>();
						if (player != null) player.ConfigureAsBot(owner, botName, difficulty);
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

		/// <summary>Host-only: re-tune every live bot to <paramref name="difficulty"/>. Returns how many bots were updated.</summary>
		public int SetAllBotsDifficulty(BotDifficulty difficulty)
		{
			if (HasStateAuthority == false) return 0;

			int updated = 0;
			for (int i = 0; i < _players.Count; i++)
			{
				var p = _players[i];
				if (p == null || p.IsBot == false) continue;
				p.SetBotDifficulty(difficulty);
				updated++;
			}
			return updated;
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_DebugAddBots(int count, BotDifficulty difficulty)
		{
			if (HasStateAuthority) AddBots(count, difficulty);
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_DebugRemoveBots(int count)
		{
			if (HasStateAuthority) RemoveBots(count);
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_DebugSetBotDifficulty(BotDifficulty difficulty)
		{
			if (HasStateAuthority) SetAllBotsDifficulty(difficulty);
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_DebugClearBots()
		{
			if (HasStateAuthority) ClearBots();
		}

		/// <summary>Spawn pose inside the player's team zone when one is assigned (Day start / Night respawn),
		/// otherwise a random spawn (Lobby joins with no team yet). The rotation is the spawn point's yaw — the
		/// direction the player will be looking on spawn (visualized by the SpawnPoint arrow gizmo).</summary>
		private void GetSpawnPoseForPlayer(PlayerRef player, out Vector3 position, out Quaternion rotation)
		{
			int zoneId = ZoneManager.Instance != null ? ZoneManager.Instance.ZoneIdOfPlayer(player) : -1;
			GetSpawnPose(zoneId, out position, out rotation);
		}

		private void GetSpawnPose(out Vector3 position, out Quaternion rotation) => GetSpawnPose(-1, out position, out rotation);

		private void GetSpawnPose(int zoneId, out Vector3 position, out Quaternion rotation)
		{
			// HorrorTown: when a procedural run is active, the team lands in the run's generated start area
			// rather than at an authored scene spawn point. The city is different every run, so there is nothing
			// to author — RunDirector picks open ground in an outer district and everyone drops in together.
			if (TryGetRunStartPose(out position, out rotation))
				return;

			var spawnPoint = PickSpawnPoint(zoneId);
			if (spawnPoint == null)
			{
				position = Vector3.up * 2f;
				rotation = Quaternion.identity;
				return;
			}

			var randomPositionOffset = Random.insideUnitCircle * spawnPoint.Radius;
			position = spawnPoint.transform.position + new Vector3(randomPositionOffset.x, 0f, randomPositionOffset.y);

			// Snap to the floor: cast down from just above the spawn point. Keep the origin
			// low (below any ceiling) so an indoor spawn point can't have its downward ray
			// hit the roof and snap the player onto it. The small lift still clears a spawn
			// point sitting flush with or just under the floor surface.
			Vector3 castOrigin = position + Vector3.up * 0.5f;
			if (Physics.Raycast(castOrigin, Vector3.down, out RaycastHit hit, 25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
			{
				position.y = hit.point.y + 0.5f;
			}

			// Face the way the spawn point's arrow gizmo points. Derive yaw from the world-space forward
			// projected onto the horizontal plane (NOT eulerAngles.y, which diverges from the actual
			// forward direction whenever the spawn point or a parent has any pitch/roll) so the player's
			// facing matches the drawn arrow exactly, regardless of parent transforms.
			Vector3 forward = spawnPoint.transform.forward;
			forward.y = 0f;
			float yaw = forward.sqrMagnitude > 0.0001f ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg : 0f;
			rotation = Quaternion.Euler(0f, yaw, 0f);
		}

		/// <summary>
		/// Spawn pose from the active procedural run, scattered a few metres so four players do not spawn inside
		/// each other. Returns false when no run is generated (the legacy authored-scene path).
		/// </summary>
		private bool TryGetRunStartPose(out Vector3 position, out Quaternion rotation)
		{
			position = default;
			rotation = Quaternion.identity;

			var director = Starter.Run.RunDirector.Instance;
			if (director == null || !director.TryGetStartPose(out position, out rotation)) return false;

			var scatter = Random.insideUnitCircle * 4f;
			position += new Vector3(scatter.x, 0f, scatter.y);

			// Same floor snap as the authored path, so a start area on a raised plaza still puts feet on ground.
			if (Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 30f,
					Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
			{
				position.y = hit.point.y + 0.5f;
			}

			return true;
		}

		// Random spawn point belonging to the requested zone (spawn points are tied to zones by containment via
		// ZoneManager); falls back to any spawn point in the scene when zoneId is negative or the zone has none.
		private SpawnPoint PickSpawnPoint(int zoneId)
		{
			if (_spawnPoints == null || _spawnPoints.Length == 0) return null;

			if (zoneId >= 0 && ZoneManager.Instance != null)
			{
				var zone = ZoneManager.Instance.ZoneById(zoneId);
				if (zone != null && zone.SpawnPoints.Count > 0)
					return zone.SpawnPoints[Random.Range(0, zone.SpawnPoints.Count)];
			}
			return _spawnPoints[Random.Range(0, _spawnPoints.Length)];
		}
	}
}
