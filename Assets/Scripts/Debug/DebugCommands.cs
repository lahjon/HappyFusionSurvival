using QFSW.QC;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Quantum Console debug commands for the Shooter mode. These are local entry points that route into the
	/// networked simulation via RPCs so they replicate correctly whether you run them on the host or a client:
	/// <list type="bullet">
	/// <item><c>add_money &lt;int&gt;</c> — grant money to the local player (state-authority replicated).</item>
	/// <item><c>add_scraps &lt;int&gt;</c> — grant crafting scraps to the local player (state-authority replicated).</item>
	/// <item><c>time_scale &lt;float&gt;</c> — set the game speed on every peer (networked via the state authority).</item>
	/// <item><c>set_daytime</c> / <c>set_nighttime</c> — force the match phase AND visual day/night cycle.</item>
	/// <item><c>trigger_night</c> — fast-forward Day + sun to ~1s before the first siren, then let them advance so you can watch the sunset and NPCs retreat.</item>
	/// <item><c>trigger_victory</c> — (Night only) instant-win for the local player's team; everyone else loses.</item>
	/// </list>
	/// </summary>
	public static class DebugCommands
	{
		[Command("add_money", "Grants money to the local player. Replicated via the state authority.")]
		private static string AddMoney(int amount)
		{
			var player = FindLocalPlayer();
			if (player == null) return "add_money: no local player found (not in a match?).";
			if (amount <= 0) return $"add_money: amount must be positive (got {amount}).";

			player.RPC_DebugAddMoney(amount);
			return $"add_money: requested +{amount} for local player.";
		}

		[Command("add_scraps", "Grants crafting scraps to the local player. Replicated via the state authority.")]
		private static string AddScraps(int amount)
		{
			var player = FindLocalPlayer();
			if (player == null) return "add_scraps: no local player found (not in a match?).";
			if (amount <= 0) return $"add_scraps: amount must be positive (got {amount}).";

			player.RPC_DebugAddScraps(amount);
			return $"add_scraps: requested +{amount} for local player.";
		}

		[Command("heal", "Heals the local player by N health (clamped to max). Replicated via the state authority.")]
		private static string Heal(int amount)
		{
			var player = FindLocalPlayer();
			if (player == null) return "heal: no local player found (not in a match?).";
			if (player.Health == null) return "heal: local player has no Health component.";
			if (amount <= 0) return $"heal: amount must be positive (got {amount}).";
			if (player.Health.IsAlive == false) return "heal: local player is dead — nothing to heal.";

			player.Health.RPC_DebugHeal(amount);
			return $"heal: requested +{amount} health for the local player.";
		}

		[Command("take_damage", "Deals N unattributed damage to the local player (ignores PvP phase/team gate). Replicated via the state authority.")]
		private static string TakeDamage(int amount)
		{
			var player = FindLocalPlayer();
			if (player == null) return "take_damage: no local player found (not in a match?).";
			if (player.Health == null) return "take_damage: local player has no Health component.";
			if (amount <= 0) return $"take_damage: amount must be positive (got {amount}).";
			if (player.Health.IsAlive == false) return "take_damage: local player is already dead.";

			player.Health.RPC_DebugTakeDamage(amount);
			return $"take_damage: requested {amount} damage to the local player.";
		}

		[Command("set_stamina", "Sets the local player's stamina. Negative value maxes it out (9999). Replicated via the state authority.")]
		private static string SetStamina(int value)
		{
			var player = FindLocalPlayer();
			if (player == null) return "set_stamina: no local player found (not in a match?).";

			player.RPC_DebugSetStamina(value);
			return value < 0
				? "set_stamina: requested max stamina (9999) for the local player."
				: $"set_stamina: requested stamina = {value} for the local player.";
		}

		[Command("add_item", "Gives the named item to the local player — fills the hotbar first, drops any overflow in front. Name auto-completes from the item database.")]
		private static string AddItem([ItemName] string itemName, int count = 1)
		{
			if (string.IsNullOrWhiteSpace(itemName)) return "add_item: provide an item name (start typing for autocomplete).";
			if (count <= 0) return $"add_item: count must be positive (got {count}).";
			if (ItemDatabase.Instance == null) return "add_item: no ItemDatabase bound (not in a match?).";

			var player = FindLocalPlayer();
			if (player == null) return "add_item: no local player found (not in a match?).";

			var inventory = player.GetComponent<Inventory>();
			if (inventory == null) return "add_item: local player has no Inventory component.";

			var def = FindItem(itemName);
			if (def == null) return $"add_item: no item named '{itemName}' in the database.";

			short amount = (short)Mathf.Clamp(count, 1, 999);
			inventory.RPC_DebugGiveItem(def.Id, amount);
			return $"add_item: requested {amount}× '{def.DisplayName}' (id {def.Id}) for the local player.";
		}

		[Command("refill_ammo", "Refills ammo for every weapon in the local player's hotbar — tops each ammo type up to a full stack and reloads all magazines. Replicated via the state authority.")]
		private static string RefillAmmo()
		{
			var player = FindLocalPlayer();
			if (player == null) return "refill_ammo: no local player found (not in a match?).";

			var inventory = player.GetComponent<Inventory>();
			if (inventory == null) return "refill_ammo: local player has no Inventory component.";

			inventory.RPC_DebugRefillAmmo();
			return "refill_ammo: requested an ammo refill for the local player's weapons.";
		}

		/// <summary>Resolve a typed name to an item: DisplayName first (what autocomplete offers), then asset name; case-insensitive.</summary>
		private static ItemData FindItem(string name)
		{
			var all = ItemDatabase.Instance.All;
			for (int i = 0; i < all.Count; i++)
				if (all[i] != null && string.Equals(all[i].DisplayName, name, System.StringComparison.OrdinalIgnoreCase))
					return all[i];
			for (int i = 0; i < all.Count; i++)
				if (all[i] != null && string.Equals(all[i].name, name, System.StringComparison.OrdinalIgnoreCase))
					return all[i];
			return null;
		}

		[Command("add_bot", "Spawns N AI bot players on enemy teams so a solo player has someone to fight. Default 1 bot at Medium difficulty (Retarded/Easy/Medium/Hard). Routed to the host.")]
		private static string AddBot(int count = 1, BotDifficulty difficulty = BotDifficulty.Medium)
		{
			if (count <= 0) return $"add_bot: count must be positive (got {count}).";
			var gm = Object.FindAnyObjectByType<GameManager>();
			if (gm == null) return "add_bot: no GameManager found (not in a match?).";

			gm.RPC_DebugAddBots(count, difficulty);
			return $"add_bot: requested {count} {difficulty} bot(s) on the host.";
		}

		[Command("remove_bot", "Despawns N AI bots, most-recently-added first. Default 1. Routed to the host.")]
		private static string RemoveBot(int count = 1)
		{
			if (count <= 0) return $"remove_bot: count must be positive (got {count}).";
			var gm = Object.FindAnyObjectByType<GameManager>();
			if (gm == null) return "remove_bot: no GameManager found (not in a match?).";

			gm.RPC_DebugRemoveBots(count);
			return $"remove_bot: requested removal of {count} bot(s) on the host.";
		}

		[Command("clear_bots", "Despawns every AI bot. Routed to the host.")]
		private static string ClearBots()
		{
			var gm = Object.FindAnyObjectByType<GameManager>();
			if (gm == null) return "clear_bots: no GameManager found (not in a match?).";

			gm.RPC_DebugClearBots();
			return "clear_bots: requested removal of all bots on the host.";
		}

		[Command("bot_difficulty", "Re-tunes ALL current AI bots to a difficulty tier: 0=Retarded, 1=Easy, 2=Medium, 3=Hard. Out-of-range values clamp to the nearest tier. Routed to the host.")]
		private static string SetBotDifficulty(int level)
		{
			var gm = Object.FindAnyObjectByType<GameManager>();
			if (gm == null) return "bot_difficulty: no GameManager found (not in a match?).";

			var difficulty = (BotDifficulty)Mathf.Clamp(level, 0, (int)BotDifficulty.Hard);
			gm.RPC_DebugSetBotDifficulty(difficulty);
			return $"bot_difficulty: set all bots to {difficulty} (level {(int)difficulty}).";
		}

		[Command("time_scale", "Sets the networked game speed (Time.timeScale) on every peer. 1 = normal, 0 = paused, >1 = faster. Replicated via the state authority.")]
		private static string TimeScale(float scale = 1f)
		{
			if (scale < 0f) return $"time_scale: scale can't be negative (got {scale}).";

			var match = MatchManager.Instance;
			if (match == null) return "time_scale: no MatchManager found (not in a match?).";

			match.RPC_DebugSetTimeScale(scale);
			return $"time_scale: requested Time.timeScale = {scale:0.##} on every peer.";
		}

		[Command("force_hunt", "Forces The Hunt event NOW (must be Night). -1 = auto-pick the most passive team as hunter. Replicated.")]
		private static string ForceHunt(int teamId = -1)
		{
			var host = GameHostManager.Instance;
			if (host == null) return "force_hunt: no GameHostManager found (not in a match / component not on the scene object?).";

			host.RPC_DebugForceHunt(teamId);
			return teamId < 0
				? "force_hunt: requested auto-pick of the most passive team."
				: $"force_hunt: requested team {teamId} as hunter.";
		}

		[Command("force_blackout", "Forces the Blackout event NOW (must be Night) — kills all town power for its duration. Replicated.")]
		private static string ForceBlackout()
		{
			var host = GameHostManager.Instance;
			if (host == null) return "force_blackout: no GameHostManager found (not in a match?).";

			host.RPC_DebugForceBlackout();
			return "force_blackout: requested town-wide blackout.";
		}

		[Command("blackout", "Forces the LightGrid lit fraction (0 = full blackout, 1 = all lit). Edges-first; holds until power_restore. Replicated.")]
		private static string Blackout(float litFraction = 0f)
		{
			var grid = LightGrid.Instance;
			if (grid == null) return "blackout: no LightGrid found (component on the GameManager NetworkObject?).";

			float clamped = Mathf.Clamp01(litFraction);
			grid.RPC_DebugSetLitFraction(clamped);
			return $"blackout: forced lit fraction = {clamped:0.00} (outer zones dark first). Use power_restore to resume the schedule.";
		}

		[Command("power_restore", "Releases the forced blackout and powers every LightGrid zone back on. Replicated.")]
		private static string PowerRestore()
		{
			var grid = LightGrid.Instance;
			if (grid == null) return "power_restore: no LightGrid found (component on the GameManager NetworkObject?).";

			grid.RPC_DebugRestorePower();
			return "power_restore: all zones powered; phase-driven schedule resumed.";
		}

		[Command("lightgrid_debug", "Toggles the in-game LightGrid overlay (green = powered, red = dark). Arg: -1 toggle (default), 0 off, 1 on. Local view only.")]
		private static string LightGridDebug(int state = -1)
		{
			LightGrid.DebugDraw = state < 0 ? !LightGrid.DebugDraw : state > 0;

			string status = LightGrid.DebugDraw ? "ON" : "OFF";
			return LightGrid.Instance == null
				? $"lightgrid_debug: overlay {status}, but no LightGrid is in the scene yet (nothing will draw)."
				: $"lightgrid_debug: overlay {status} (green = powered, red = dark).";
		}

		[Command("arm", "Force-enables PvP regardless of phase so you can playtest combat during Day/Lobby. Stays on until disarmed. 'arm' = on, 'arm false' = off. Replicated.")]
		private static string Arm(bool on = true)
		{
			var match = MatchManager.Instance;
			if (match == null) return "arm: no MatchManager found (not in a match?).";

			match.RPC_DebugArm(on);
			return on
				? "arm: PvP force-enabled — ignores the Day/Lobby phase gate and stays on until 'arm false' (friendly fire still off)."
				: "arm: disarmed — PvP gate back to normal phase rules.";
		}

		[Command("trigger_victory", "Ends the round NOW with the local player's team as the winner (everyone else loses). Night only. Replicated.")]
		private static string TriggerVictory()
		{
			var match = MatchManager.Instance;
			if (match == null) return "trigger_victory: no MatchManager found (not in a match?).";
			if (match.Phase != MatchPhase.Night) return $"trigger_victory: only works at Night (current phase: {match.Phase}).";

			var player = FindLocalPlayer();
			if (player == null) return "trigger_victory: no local player found (not in a match?).";

			var teams = TeamManager.Instance;
			if (teams == null) return "trigger_victory: no TeamManager found.";
			if (teams.TeamOf(player.Owner) < 0) return "trigger_victory: local player isn't on a team (spectator?).";

			match.RPC_DebugTriggerVictory(player.Owner);
			return "trigger_victory: requested instant win for the local player's team.";
		}

		[Command("armstatus", "Dumps the live PvP fire-gate state for the local player (debug-arm / phase / weapon cooldown). Run it between shots to see which gate is blocking fire. Local read-only.")]
		private static string ArmStatus()
		{
			var player = FindLocalPlayer();
			if (player == null) return "armstatus: no local player found (not in a match?).";

			var match = MatchManager.Instance;
			string matchPart = match == null
				? "match=<none>"
				: $"phase={match.Phase} pvpForced={match.IsPvpForced} pvpArmed={(bool)player.PvpArmed}";

			var inv = player.GetComponent<Inventory>();
			var action = inv != null ? inv.ActiveAction : null;
			string firePart = player.ActionInvoker == null
				? "invoker=<none>"
				: $"action={(action != null ? action.name : "<null>")} canFire={player.ActionInvoker.CanFire} charging={player.ActionInvoker.IsCharging}";

			return $"armstatus: {matchPart} | {firePart}";
		}

		[Command("set_dusk", "Forces the DuskWarning phase — sounds the town siren and sends NPCs retreating to their buildings to shut the exits. Use set_nighttime next to turn them hostile, set_daytime to reset. Replicated.")]
		private static string SetDusk()
		{
			var match = MatchManager.Instance;
			if (match == null) return "set_dusk: no MatchManager found (not in a match?).";

			match.RPC_DebugForcePhase(MatchPhase.DuskWarning);
			return "set_dusk: forced DuskWarning (siren + NPC retreat). set_nighttime → hostility, set_daytime → reset.";
		}

		[Command("trigger_night", "Fast-forwards the Day phase to 1s before the first siren, then lets the round advance on its own — so you can watch the DuskWarning siren, the sunset, and every NPC retreating home in real time. Optional arg: seconds of lead time (default 1). Forces Day first if not already there. Replicated.")]
		private static string TriggerNight(float leadSeconds = 1f)
		{
			var match = MatchManager.Instance;
			if (match == null) return "trigger_night: no MatchManager found (not in a match?).";
			if (leadSeconds < 0f) return $"trigger_night: leadSeconds can't be negative (got {leadSeconds}).";

			float lead = Mathf.Max(0.1f, leadSeconds);
			match.RPC_DebugSkipToDusk(leadSeconds);

			// No need to park the sun anymore — TimeManager now drives the look straight off the match phase, so the
			// DuskWarning sunset rolls automatically once the round advances into dusk.
			return $"trigger_night: Day fast-forwarded — siren, sunset & NPC retreat play out over the next ~{lead:0.##}s + dusk window.";
		}

		[Command("set_daytime", "Forces DAY — MatchManager Day phase + TimeManager visual day. Replicated.")]
		private static string SetDaytime() => ForcePhase(night: false);

		[Command("set_nighttime", "Forces NIGHT — MatchManager Night phase + TimeManager visual night. Replicated.")]
		private static string SetNighttime() => ForcePhase(night: true);

		private static string ForcePhase(bool night)
		{
			var match = MatchManager.Instance;
			var time = TimeManager.Instance;
			if (match == null && time == null)
				return "set_phase: no MatchManager/TimeManager found (not in a match?).";

			match?.RPC_DebugForcePhase(night ? MatchPhase.Night : MatchPhase.Day);
			time?.RPC_DebugSetNight(night);

			string label = night ? "NIGHT" : "DAY";
			return $"set_{(night ? "night" : "day")}time: forced {label} (match phase{(match != null ? " ✓" : " —")}, visuals{(time != null ? " ✓" : " —")}).";
		}

		/// <summary>The Player this peer controls — the one carrying input authority.</summary>
		private static Player FindLocalPlayer()
		{
			var players = Object.FindObjectsByType<Player>(FindObjectsInactive.Exclude);
			foreach (var p in players)
			{
				if (p.HasInputAuthority) return p;
			}
			return null;
		}
	}
}
