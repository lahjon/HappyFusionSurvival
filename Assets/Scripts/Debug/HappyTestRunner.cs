using System.Linq;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Editor-only runtime applier for the Happy Hub <c>Testing</c> tab. When a play session is launched from that tab
	/// (signalled by the <c>HFS.TestConfigActive</c> SessionState flag — the same mechanism the Solo toolbar button uses
	/// for <c>HFS.ForceSinglePlayer</c>), this reads <see cref="HappyTestConfig"/> and reproduces the requested scenario:
	/// team size, bots, starting loadout, currency, phase and time-scale.
	///
	/// It deliberately reuses the existing networked entry points (<see cref="GameManager.AddBots"/>,
	/// <see cref="Inventory.AuthorityGiveItem"/>, the <c>MatchManager</c>/<c>Player</c> debug RPCs) rather than
	/// re-implementing any gameplay — it is just an automated sequence of the same actions the Quantum Console
	/// debug commands perform. Application runs only on the host (state authority); in the isolated-host Solo path the
	/// local player is the host, so everything resolves locally.
	///
	/// A no-op outside the editor and whenever the flag is unset, so a normal Play press is never affected.
	/// </summary>
	public sealed class HappyTestRunner : MonoBehaviour
	{
		private const string ActiveKey = "HFS.TestConfigActive";

		private HappyTestConfig _config;
		private bool _applied;

		/// <summary>True only in the editor when the Testing tab launched this session.</summary>
		private static bool IsActive
		{
#if UNITY_EDITOR
			get => UnityEditor.SessionState.GetBool(ActiveKey, false);
#else
			get => false;
#endif
		}

		// Set the pending team size before any scene (and thus MatchManager) loads, so the host's auto-begin picks it
		// up. MatchBootstrap is a plain static that survives scene loads on the host — the same hand-off the lobby uses.
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void ApplyEarly()
		{
			if (IsActive == false) return;
			var cfg = HappyTestConfig.Load();
			MatchBootstrap.PendingTeamSize = (int)cfg.TeamSize;
		}

		// Spawn the persistent applier once the first scene is up. It survives the lobby → game scene load (for the
		// Main Menu launch path) and applies once the match is actually running in the game scene.
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Bootstrap()
		{
			if (IsActive == false) return;
			var go = new GameObject("[HappyTestRunner]");
			DontDestroyOnLoad(go);
			go.AddComponent<HappyTestRunner>();
		}

		private void Awake() => _config = HappyTestConfig.Load();

		private void Update()
		{
			if (_applied) return;

			var match = MatchManager.Instance;
			var gm = GameManager.Instance;

			// Wait until the host is in an actually-started round (teams assigned, players spawned). MatchManager only
			// runs its phase machine on the state authority, so HasStateAuthority also confirms we are the host.
			if (match == null || gm == null) return;
			if (match.Object == null || match.Object.IsValid == false) return;
			if (match.HasStateAuthority == false) return;
			if (match.Phase == MatchPhase.Lobby) return;

			var localPlayer = FindLocalPlayer();
			if (localPlayer == null) return;

			Apply(match, gm, localPlayer);
			_applied = true;
			Destroy(gameObject);
		}

		private void Apply(MatchManager match, GameManager gm, Player localPlayer)
		{
			Debug.Log("[HappyHub] Applying test config to the running match.");

			// --- Bots (host-authoritative; lands them on enemy teams since the round is already underway) ---
			if (_config.BotCount > 0)
				gm.AddBots(_config.BotCount, _config.BotDifficulty);

			// --- Local player loadout ---
			var inventory = localPlayer.GetComponent<Inventory>();
			if (inventory != null)
			{
				if (_config.StartArmed)
					GiveByName(inventory, _config.StartingWeapon, 1);

				if (_config.StartingItems != null)
				{
					foreach (var entry in _config.StartingItems)
						GiveByName(inventory, entry.Name, Mathf.Max(1, entry.Count));
				}

				if (_config.RefillAmmo)
					inventory.AuthorityRefillAmmo();
			}

			// --- Arm the bots with the same weapon, if requested ---
			if (_config.ArmBots && string.IsNullOrWhiteSpace(_config.StartingWeapon) == false)
			{
				var weapon = FindItem(_config.StartingWeapon);
				if (weapon != null)
				{
					foreach (var p in gm.Players)
					{
						if (p == null || p.IsBot == false) continue;
						var botInv = p.GetComponent<Inventory>();
						if (botInv == null) continue;
						botInv.AuthorityGiveItem(weapon.Id, 1);
						botInv.AuthorityRefillAmmo();
					}
				}
			}

			// --- Currency (bonus on top of the prefab's StartingMoney/StartingScraps) ---
			if (_config.BonusMoney > 0)
				localPlayer.RPC_DebugAddMoney(_config.BonusMoney);
			if (_config.BonusScraps > 0)
				localPlayer.RPC_DebugAddScraps(_config.BonusScraps);

			// --- Time scale ---
			if (Mathf.Approximately(_config.TimeScale, 1f) == false && _config.TimeScale >= 0f)
				match.RPC_DebugSetTimeScale(_config.TimeScale);

			// --- PvP arm override ---
			if (_config.ForcePvpArmed)
				match.RPC_DebugArm(true);

			// --- Phase override (do this last so bot teaming / loadout settle during Day first) ---
			if (_config.StartPhase == HappyTestConfig.StartPhaseOption.Night)
			{
				match.RPC_DebugForcePhase(MatchPhase.Night);
				TimeManager.Instance?.RPC_DebugSetNight(true);
			}
		}

		private void GiveByName(Inventory inventory, string itemName, int count)
		{
			if (string.IsNullOrWhiteSpace(itemName)) return;
			var def = FindItem(itemName);
			if (def == null)
			{
				Debug.LogWarning($"[HappyHub] Test config item '{itemName}' not found in the database — skipped.");
				return;
			}
			inventory.AuthorityGiveItem(def.Id, (short)Mathf.Clamp(count, 1, 999));
		}

		/// <summary>Resolve a typed name to an item: DisplayName first, then asset name; case-insensitive (mirrors the
		/// <c>add_item</c> console command's resolution).</summary>
		private static ItemDefinition FindItem(string name)
		{
			if (ItemDatabase.Instance == null || string.IsNullOrWhiteSpace(name)) return null;
			var all = ItemDatabase.Instance.All;
			for (int i = 0; i < all.Count; i++)
				if (all[i] != null && string.Equals(all[i].DisplayName, name, System.StringComparison.OrdinalIgnoreCase))
					return all[i];
			for (int i = 0; i < all.Count; i++)
				if (all[i] != null && string.Equals(all[i].name, name, System.StringComparison.OrdinalIgnoreCase))
					return all[i];
			return null;
		}

		private static Player FindLocalPlayer()
		{
			var players = Object.FindObjectsByType<Player>(FindObjectsInactive.Exclude);
			foreach (var p in players)
				if (p.HasInputAuthority) return p;
			return null;
		}
	}
}
