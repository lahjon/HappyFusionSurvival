using QFSW.QC;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Quantum Console debug commands for the Shooter mode. These are local entry points that route into the
	/// networked simulation via RPCs so they replicate correctly whether you run them on the host or a client:
	/// <list type="bullet">
	/// <item><c>add_money &lt;int&gt;</c> — grant money to the local player (state-authority replicated).</item>
	/// <item><c>add_scraps &lt;int&gt;</c> — grant crafting scraps to the local player (state-authority replicated).</item>
	/// <item><c>set_time &lt;int&gt;</c> — set <see cref="Time.timeScale"/> (local game speed; not networked).</item>
	/// <item><c>set_daytime</c> / <c>set_nighttime</c> — force the match phase AND visual day/night cycle.</item>
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

		[Command("add_bot", "Spawns N AI bot players on enemy teams so a solo player has someone to fight. Default 1. Routed to the host.")]
		private static string AddBot(int count = 1)
		{
			if (count <= 0) return $"add_bot: count must be positive (got {count}).";
			var gm = Object.FindAnyObjectByType<GameManager>();
			if (gm == null) return "add_bot: no GameManager found (not in a match?).";

			gm.RPC_DebugAddBots(count);
			return $"add_bot: requested {count} bot(s) on the host.";
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

		[Command("set_time", "Sets Time.timeScale (local game speed). 1 = normal, 0 = paused, >1 = faster. Not networked.")]
		private static string SetTime(int timeScale)
		{
			if (timeScale < 0) return $"set_time: timeScale can't be negative (got {timeScale}).";
			Time.timeScale = timeScale;
			return $"set_time: Time.timeScale = {timeScale}";
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

		[Command("arm", "Force-enables PvP for <seconds> regardless of phase (override no-combat-during-Day). Default 60s; 0 = disarm. Replicated.")]
		private static string Arm(int seconds = 60)
		{
			var match = MatchManager.Instance;
			if (match == null) return "arm: no MatchManager found (not in a match?).";

			match.RPC_DebugArm(seconds);
			return seconds > 0
				? $"arm: PvP force-enabled for {seconds}s (ignores the Day/Lobby phase gate; friendly fire still off)."
				: "arm: disarmed — PvP gate back to normal phase rules.";
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
			var players = Object.FindObjectsByType<Player>(FindObjectsSortMode.None);
			foreach (var p in players)
			{
				if (p.HasInputAuthority) return p;
			}
			return null;
		}
	}
}
