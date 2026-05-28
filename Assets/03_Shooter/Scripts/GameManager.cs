using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Starter.Common;
using Starter.Common.Crafting;
using Starter.Common.Inventory;

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

		[Header("World Generation")]
		[Tooltip("Deterministic seed for procedural loot rolls. 0 = pick a random seed per session.")]
		[SerializeField] private int _seedOverride;

		[Header("Sleep Skip")]
		[Tooltip("Seconds the screen fades to black before the time jump to morning.")]
		[Min(0f)] public float SleepFadeInDuration = 1f;
		[Tooltip("Seconds the screen fades back from black after the time jump.")]
		[Min(0f)] public float SleepFadeOutDuration = 1f;

		[Networked]
		public PlayerRef BestHunter { get; set; }

		[Networked]
		public int NetworkedWorldSeed { get; set; }

		/// <summary>Current phase of the "everyone asleep -> morning" fade sequence. Driven by state authority; read by <see cref="SleepFadeOverlay"/> on every client to draw the fade.</summary>
		[Networked]
		public SleepPhase CurrentSleepPhase { get; private set; }

		/// <summary>TickTimer for the current <see cref="CurrentSleepPhase"/>. Used by clients to compute fade alpha each frame.</summary>
		[Networked]
		public TickTimer SleepPhaseTimer { get; private set; }

		public Player LocalPlayer { get; private set; }

		private List<Player> _players = new(32);
		private SpawnPoint[] _spawnPoints;

		private void Awake()
		{
			// Bind on every client (authority + remotes) so item lookups work without Resources.
			if (ItemDatabase != null)
				ItemDatabase.Bind();
			if (RecipeDatabase != null)
				RecipeDatabase.Bind();

			// Awake fires before any Fusion Spawned() so scene-placed PickupableItem /
			// LootContainer rolls see a valid seed during their own Spawned().
			// Host & client both run this — the host's value is the authoritative one;
			// the client's local pick is overwritten in Spawned() from the [Networked] mirror.
			WorldGen.Seed = _seedOverride != 0 ? _seedOverride : unchecked((int)System.DateTime.UtcNow.Ticks);
		}

		public override void Spawned()
		{
			_spawnPoints = FindObjectsOfType<SpawnPoint>();

			if (HasStateAuthority)
				NetworkedWorldSeed = WorldGen.Seed;
			else
				WorldGen.Seed = NetworkedWorldSeed;
		}

		public override void FixedUpdateNetwork()
		{
			BestHunter = PlayerRef.None;
			int bestHunterKills = 0;

			for (int i = 0; i < _players.Count; i++)
			{
				var player = _players[i];

				if (player.KCC.Position.y < -15f)
				{
					// Player fell, let's kill him
					player.Health.TakeHit(1000);
				}

				if (player.Health.IsFinished)
				{
					player.Respawn(GetSpawnPosition());
				}

				// Calculate the best hunter
				if (player.Health.IsAlive && player.ChickenKills > bestHunterKills)
				{
					bestHunterKills = player.ChickenKills;
					BestHunter = player.Object.InputAuthority;
				}
			}

			if (HasStateAuthority)
			{
				TickSleepSkip();
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
			var beds = FindObjectsByType<Bed>(FindObjectsSortMode.None);
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
			if (HasStateAuthority == false)
				return;

			var player = Runner.Spawn(PlayerPrefab, GetSpawnPosition(), Quaternion.identity, playerRef);
			Runner.SetPlayerObject(playerRef, player.Object);

			// This list is state authority only,
			// so it is valid to have this list non-networked
			_players.Add(player);
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

		private Vector3 GetSpawnPosition()
		{
			var spawnPoint = _spawnPoints[Random.Range(0, _spawnPoints.Length)];
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
	}
}
