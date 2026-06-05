using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// User-specific test launch settings edited by the Happy Hub editor window's <c>Testing</c> tab and consumed at
	/// runtime by <see cref="HappyTestRunner"/>. This is the data side of "extend the Solo Play button" — it captures
	/// the scenario you want to drop into (team size, bots, starting loadout, phase, …) so a single click reproduces it.
	///
	/// Persisted as JSON under <c>UserSettings/</c> (already git-ignored, so the config is per-developer and never
	/// committed). Stored by item <i>name</i> rather than object reference so the file stays plain JSON — names resolve
	/// against <see cref="Starter.Common.Inventory.ItemDatabase"/> at apply time, exactly like the <c>add_item</c> console command.
	///
	/// Not a ScriptableObject on purpose: keeping it as a plain serializable object out of the Assets database is what
	/// lets it be a git-ignored, user-local file with no .meta to manage.
	/// </summary>
	[Serializable]
	public class HappyTestConfig
	{
		public enum TeamSizeOption { Solo = 1, Duo = 2, Trio = 3 }
		public enum StartSceneOption { HappyTown = 0, MainMenu = 1 }
		public enum StartPhaseOption { Day = 0, Night = 1 }

		[Serializable]
		public struct StartingItemEntry
		{
			public string Name;
			public int Count;
		}

		// ---- Launch ----
		public TeamSizeOption TeamSize = TeamSizeOption.Solo;
		public StartSceneOption StartScene = StartSceneOption.HappyTown;

		// ---- Bots ----
		public int BotCount = 0;
		public bool ArmBots = false;

		// ---- Local player loadout ----
		public bool StartArmed = false;
		public string StartingWeapon = "";
		public List<StartingItemEntry> StartingItems = new();
		public bool RefillAmmo = true;

		// ---- Currency (bonus granted on start; <= 0 = none) ----
		public int BonusMoney = 0;
		public int BonusScraps = 0;

		// ---- Phase / combat ----
		public StartPhaseOption StartPhase = StartPhaseOption.Day;
		public bool ForcePvpArmed = false;
		public float TimeScale = 1f;

		// =====================================================================
		// Persistence
		// =====================================================================

		// UserSettings/ is already git-ignored (see .gitignore), so anything here is per-developer and uncommitted.
		private static string FilePath =>
			Path.Combine(Application.dataPath, "..", "UserSettings", "HappyHub", "TestConfig.json");

		/// <summary>Load the saved config, or a fresh default instance if none exists / the file is unreadable.</summary>
		public static HappyTestConfig Load()
		{
			try
			{
				var path = FilePath;
				if (File.Exists(path))
				{
					var json = File.ReadAllText(path);
					var cfg = JsonUtility.FromJson<HappyTestConfig>(json);
					if (cfg != null)
					{
						cfg.StartingItems ??= new List<StartingItemEntry>();
						return cfg;
					}
				}
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[HappyHub] Failed to load test config: {e.Message}");
			}
			return new HappyTestConfig();
		}

		/// <summary>Write the config to disk (creates the UserSettings/HappyHub folder if needed).</summary>
		public void Save()
		{
			try
			{
				var path = FilePath;
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(path, JsonUtility.ToJson(this, true));
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[HappyHub] Failed to save test config: {e.Message}");
			}
		}
	}
}
