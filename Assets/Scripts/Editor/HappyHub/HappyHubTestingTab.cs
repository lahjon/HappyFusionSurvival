using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Starter.Common.Inventory;
using UnityEditor;
using UnityEngine;

namespace Starter.Shooter.EditorTools
{
	/// <summary>
	/// The Happy Hub <c>Testing</c> tab — the editable surface for the Solo-Play test scenario. It is decoupled from the
	/// plain <see cref="HappyTestConfig"/> data class (decorated editor fields here, attribute-free serializable data
	/// there) so the on-disk format stays a simple, git-ignored JSON file. <see cref="ToConfig"/> / <see cref="FromConfig"/>
	/// bridge the two; <see cref="HappyTestLauncher.Launch"/> saves + enters Play mode with the scenario armed.
	/// </summary>
	[Serializable]
	public class HappyHubTestingTab
	{
		[TitleGroup("Launch")]
		[EnumToggleButtons, LabelText("Team Size")]
		public HappyTestConfig.TeamSizeOption TeamSize = HappyTestConfig.TeamSizeOption.Solo;

		[TitleGroup("Launch")]
		[EnumToggleButtons, LabelText("Start Scene")]
		[InfoBox("HappyTown launches as an isolated host (Solo Play). Main Menu goes through the normal lobby flow; the " +
			"test scenario still applies once the game scene loads, but team size is taken from the lobby.",
			InfoMessageType.None)]
		public HappyTestConfig.StartSceneOption StartScene = HappyTestConfig.StartSceneOption.HappyTown;

		[TitleGroup("Launch")]
		[EnumToggleButtons, LabelText("Start Phase")]
		public HappyTestConfig.StartPhaseOption StartPhase = HappyTestConfig.StartPhaseOption.Day;

		[TitleGroup("Bots")]
		[PropertyRange(0, 17), LabelText("Bot Count")]
		public int BotCount;

		[TitleGroup("Bots")]
		[ToggleLeft, LabelText("Arm bots with the starting weapon")]
		public bool ArmBots;

		[TitleGroup("Loadout")]
		[ToggleLeft, LabelText("Start Armed")]
		public bool StartArmed;

		[TitleGroup("Loadout")]
		[ShowIf(nameof(StartArmed))]
		[ValueDropdown(nameof(AllItemNames)), LabelText("Starting Weapon")]
		public string StartingWeapon = "";

		[TitleGroup("Loadout")]
		[LabelText("Extra Items"), ListDrawerSettings(ShowFoldout = true)]
		public List<HappyTestConfig.StartingItemEntry> StartingItems = new();

		[TitleGroup("Loadout")]
		[ToggleLeft, LabelText("Refill ammo on start")]
		public bool RefillAmmo = true;

		[TitleGroup("Economy")]
		[MinValue(0), LabelText("Bonus Money")]
		public int BonusMoney;

		[TitleGroup("Economy")]
		[MinValue(0), LabelText("Bonus Scraps")]
		public int BonusScraps;

		[TitleGroup("Simulation")]
		[PropertyRange(0f, 4f), LabelText("Time Scale")]
		public float TimeScale = 1f;

		[TitleGroup("Simulation")]
		[ToggleLeft, LabelText("Force PvP armed (combat during Day)")]
		public bool ForcePvpArmed;

		private bool _loaded;

		// Pull the last-saved config in when the inspector tree first builds. Guarded so a tree rebuild never clobbers
		// in-progress edits.
		[OnInspectorInit]
		private void Init()
		{
			if (_loaded) return;
			FromConfig(HappyTestConfig.Load());
			_loaded = true;
		}

		[PropertySpace(8)]
		[Button("▶  Play with these settings", ButtonSizes.Large), GUIColor(0.5f, 0.85f, 0.55f)]
		[DisableInPlayMode]
		private void PlayWithSettings()
		{
			HappyTestLauncher.Launch(ToConfig());
		}

		[HorizontalGroup("io")]
		[Button("Save Preset")]
		private void SaveToDisk()
		{
			ToConfig().Save();
			ShowNotification("Saved");
		}

		[HorizontalGroup("io")]
		[Button("Reload")]
		private void Reload()
		{
			FromConfig(HappyTestConfig.Load());
		}

		[HorizontalGroup("io")]
		[Button("Reset Defaults")]
		private void ResetDefaults()
		{
			FromConfig(new HappyTestConfig());
		}

		// =====================================================================
		// Live actions (act on the running local player; route through the same RPCs as the console commands)
		// =====================================================================

		[TitleGroup("Live (in Play Mode)")]
		[InfoBox("Enter Play mode (e.g. via the button above) to use these on the running player.",
			InfoMessageType.None, VisibleIf = "@!" + nameof(Playing))]
		[HorizontalGroup("Live (in Play Mode)/money"), LabelText("Money"), MinValue(1)]
		public int LiveMoney = 500;

		[HorizontalGroup("Live (in Play Mode)/money", Width = 110)]
		[Button("Add Money"), EnableIf(nameof(Playing))]
		private void AddMoney() => LocalPlayer()?.RPC_DebugAddMoney(Mathf.Max(1, LiveMoney));

		[TitleGroup("Live (in Play Mode)")]
		[Button("Refill Ammo"), EnableIf(nameof(Playing))]
		private void RefillAmmoNow() => LocalInventory()?.RPC_DebugRefillAmmo();

		[TitleGroup("Live (in Play Mode)")]
		[HorizontalGroup("Live (in Play Mode)/item"), LabelText("Item"), AssetsOnly]
		public ItemDefinition LiveItem;

		[HorizontalGroup("Live (in Play Mode)/item", Width = 70), LabelText("Count"), MinValue(1)]
		public int LiveItemCount = 1;

		[HorizontalGroup("Live (in Play Mode)/item", Width = 110)]
		[Button("Add Item"), EnableIf(nameof(Playing))]
		private void AddItem()
		{
			if (LiveItem == null)
			{
				Debug.LogWarning("[HappyHub] Drag an Item Definition into the Item slot first.");
				return;
			}
			LocalInventory()?.RPC_DebugGiveItem(LiveItem.Id, (short)Mathf.Clamp(LiveItemCount, 1, 999));
		}

		private static bool Playing => Application.isPlaying;

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

		// =====================================================================
		// Bridging
		// =====================================================================

		public HappyTestConfig ToConfig() => new()
		{
			TeamSize = TeamSize,
			StartScene = StartScene,
			StartPhase = StartPhase,
			BotCount = BotCount,
			ArmBots = ArmBots,
			StartArmed = StartArmed,
			StartingWeapon = StartingWeapon,
			StartingItems = new List<HappyTestConfig.StartingItemEntry>(StartingItems),
			RefillAmmo = RefillAmmo,
			BonusMoney = BonusMoney,
			BonusScraps = BonusScraps,
			TimeScale = TimeScale,
			ForcePvpArmed = ForcePvpArmed,
		};

		public void FromConfig(HappyTestConfig c)
		{
			if (c == null) return;
			TeamSize = c.TeamSize;
			StartScene = c.StartScene;
			StartPhase = c.StartPhase;
			BotCount = c.BotCount;
			ArmBots = c.ArmBots;
			StartArmed = c.StartArmed;
			StartingWeapon = c.StartingWeapon;
			StartingItems = c.StartingItems != null
				? new List<HappyTestConfig.StartingItemEntry>(c.StartingItems)
				: new List<HappyTestConfig.StartingItemEntry>();
			RefillAmmo = c.RefillAmmo;
			BonusMoney = c.BonusMoney;
			BonusScraps = c.BonusScraps;
			TimeScale = c.TimeScale;
			ForcePvpArmed = c.ForcePvpArmed;
		}

		private static void ShowNotification(string msg)
		{
			var w = EditorWindow.focusedWindow;
			if (w != null) w.ShowNotification(new GUIContent(msg));
		}

		// =====================================================================
		// Item dropdown (edit-mode: enumerate ItemDefinition assets directly, since ItemDatabase only binds at runtime)
		// =====================================================================

		private static IEnumerable<string> AllItemNames()
		{
			var names = new List<string>();
			var guids = AssetDatabase.FindAssets("t:" + nameof(ItemDefinition));
			foreach (var guid in guids)
			{
				var path = AssetDatabase.GUIDToAssetPath(guid);
				var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
				if (def == null) continue;
				var display = !string.IsNullOrEmpty(def.DisplayName) ? def.DisplayName : def.name;
				if (!names.Contains(display)) names.Add(display);
			}
			names.Sort(StringComparer.OrdinalIgnoreCase);
			return names;
		}
	}
}
