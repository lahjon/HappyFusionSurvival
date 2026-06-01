using System.Collections.Generic;
using System.Text;
using Starter.Common.Inventory;
using UnityEditor;
using UnityEngine;

namespace Starter.Shooter.EditorTools
{
	/// <summary>
	/// Editor utility that keeps item ids sane. Item <see cref="ItemDefinition.Id"/> is a stable
	/// network key (InventorySlot.ItemId is a short), so duplicates silently drop an item at runtime
	/// (ItemDatabase.BuildLookup logs and skips the collision). This validator reports duplicates and
	/// zero ids across the ItemDatabase and auto-assigns the smallest free positive id to each,
	/// keeping the first occurrence of any value. Run after adding items.
	/// </summary>
	public static class ItemDatabaseValidator
	{
		[MenuItem("Tools/Inventory/Validate Item Database")]
		public static void Validate()
		{
			var dbGuids = AssetDatabase.FindAssets("t:" + nameof(ItemDatabase));
			if (dbGuids.Length == 0)
			{
				Debug.LogError("[ItemDatabaseValidator] No ItemDatabase asset found.");
				return;
			}
			if (dbGuids.Length > 1)
				Debug.LogWarning($"[ItemDatabaseValidator] {dbGuids.Length} ItemDatabase assets found — validating the first.");

			var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(AssetDatabase.GUIDToAssetPath(dbGuids[0]));
			var items = db.All;

			var used = new HashSet<short>();
			short NextFreeId()
			{
				short c = 1;
				while (used.Contains(c)) c++;
				return c;
			}

			int fixedCount = 0;
			var report = new StringBuilder();

			foreach (var item in items)
			{
				if (item == null) continue;

				if (item.Id != 0 && used.Add(item.Id))
					continue; // first time we've seen this non-zero id — keep it

				short oldId = item.Id;
				short newId = NextFreeId();
				used.Add(newId);
				item.Id = newId;
				EditorUtility.SetDirty(item);
				fixedCount++;
				report.AppendLine($"  {item.name}: Id {oldId} -> {newId} ({(oldId == 0 ? "was unset" : "was duplicate")})");
			}

			if (fixedCount > 0)
			{
				AssetDatabase.SaveAssets();
				Debug.LogWarning($"[ItemDatabaseValidator] Reassigned {fixedCount} id(s):\n{report}");
			}
			else
			{
				Debug.Log($"[ItemDatabaseValidator] OK — {used.Count} items, all ids unique and non-zero.");
			}
		}
	}
}
