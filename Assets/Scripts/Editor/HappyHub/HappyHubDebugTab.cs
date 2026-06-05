using System;
using Sirenix.OdinInspector;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter.EditorTools
{
	/// <summary>
	/// The Happy Hub <c>Debug</c> tab — one-click versions of the Quantum Console debug commands, usable live while
	/// playing. Every action routes through the exact same networked entry points the console commands use
	/// (<c>GameManager.RPC_DebugAddBots</c>, <c>MatchManager.RPC_Debug*</c>, <c>Player.RPC_Debug*</c>, …), so behaviour is
	/// identical whether you trigger it here or from the console. Buttons are disabled outside Play mode.
	///
	/// Odin grouping note: a '/' in a group name denotes nesting, so each horizontal row's id must be the parent group's
	/// exact name + "/row" (e.g. "Bots/row" nests under TitleGroup "Bots").
	/// </summary>
	[Serializable]
	public class HappyHubDebugTab
	{
		private static bool Playing => Application.isPlaying;

		[TitleGroup("Bots")]
		[InfoBox("These act on the running match. Enter Play mode (e.g. via the Testing tab) to use them.",
			InfoMessageType.None, VisibleIf = "@!" + nameof(Playing))]
		[HorizontalGroup("Bots/row"), Button("+1 Bot"), EnableIf(nameof(Playing))]
		private void AddBot() => GameManager.Instance?.RPC_DebugAddBots(1);

		[HorizontalGroup("Bots/row"), Button("+3 Bots"), EnableIf(nameof(Playing))]
		private void AddThreeBots() => GameManager.Instance?.RPC_DebugAddBots(3);

		[HorizontalGroup("Bots/row"), Button("Remove Bot"), EnableIf(nameof(Playing))]
		private void RemoveBot() => GameManager.Instance?.RPC_DebugRemoveBots(1);

		[HorizontalGroup("Bots/row"), Button("Clear Bots"), EnableIf(nameof(Playing))]
		private void ClearBots() => GameManager.Instance?.RPC_DebugClearBots();

		[TitleGroup("Phase & Combat")]
		[HorizontalGroup("Phase & Combat/row"), Button("Force Day"), EnableIf(nameof(Playing))]
		private void ForceDay()
		{
			MatchManager.Instance?.RPC_DebugForcePhase(MatchPhase.Day);
			TimeManager.Instance?.RPC_DebugSetNight(false);
		}

		[HorizontalGroup("Phase & Combat/row"), Button("Force Night"), EnableIf(nameof(Playing))]
		private void ForceNight()
		{
			MatchManager.Instance?.RPC_DebugForcePhase(MatchPhase.Night);
			TimeManager.Instance?.RPC_DebugSetNight(true);
		}

		[HorizontalGroup("Phase & Combat/row"), Button("Arm PvP"), EnableIf(nameof(Playing))]
		private void Arm() => MatchManager.Instance?.RPC_DebugArm(true);

		[HorizontalGroup("Phase & Combat/row"), Button("Disarm"), EnableIf(nameof(Playing))]
		private void Disarm() => MatchManager.Instance?.RPC_DebugArm(false);

		[TitleGroup("Local Player")]
		[HorizontalGroup("Local Player/row"), Button("Refill Ammo"), EnableIf(nameof(Playing))]
		private void RefillAmmo() => LocalInventory()?.RPC_DebugRefillAmmo();

		[HorizontalGroup("Local Player/row"), Button("Heal 100"), EnableIf(nameof(Playing))]
		private void Heal() => LocalPlayer()?.Health?.RPC_DebugHeal(100);

		[HorizontalGroup("Local Player/row"), Button("+500 Money"), EnableIf(nameof(Playing))]
		private void Money() => LocalPlayer()?.RPC_DebugAddMoney(500);

		[HorizontalGroup("Local Player/row"), Button("+500 Scraps"), EnableIf(nameof(Playing))]
		private void Scraps() => LocalPlayer()?.RPC_DebugAddScraps(500);

		[TitleGroup("Simulation")]
		[PropertyRange(0f, 4f), HideLabel, SuffixLabel("× time", true)]
		public float TimeScale = 1f;

		[TitleGroup("Simulation")]
		[Button("Apply Time Scale"), EnableIf(nameof(Playing))]
		private void ApplyTimeScale() => MatchManager.Instance?.RPC_DebugSetTimeScale(TimeScale);

		// ---- helpers ----

		private static Player LocalPlayer()
		{
			var players = UnityEngine.Object.FindObjectsByType<Player>(FindObjectsInactive.Exclude);
			foreach (var p in players)
				if (p.HasInputAuthority) return p;
			return null;
		}

		private static Inventory LocalInventory()
		{
			var p = LocalPlayer();
			return p != null ? p.GetComponent<Inventory>() : null;
		}
	}
}
