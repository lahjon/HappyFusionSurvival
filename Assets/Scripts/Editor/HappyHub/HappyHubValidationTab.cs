using System;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace Starter.Shooter.EditorTools
{
	/// <summary>
	/// The Happy Hub <c>Validation</c> tab — shortcuts to the project's data-integrity tools. Currently surfaces the item
	/// database validator (Tools/Inventory/Validate Item Database). Scaffolded to grow into a single "run all checks"
	/// pre-playtest panel (recipes, quests, missing item registrations, …).
	/// </summary>
	[Serializable]
	public class HappyHubValidationTab
	{
		[InfoBox("Run before a playtest to catch unregistered items / bad ids before they fail silently at spawn time.",
			InfoMessageType.None)]
		[Button("Validate Item Database", ButtonSizes.Large)]
		private void ValidateItemDatabase()
		{
			if (!EditorApplication.ExecuteMenuItem("Tools/Inventory/Validate Item Database"))
				Debug.LogWarning("[HappyHub] Couldn't find menu item 'Tools/Inventory/Validate Item Database'.");
		}

		[Button("Ping Item Database Asset")]
		private void PingItemDatabase()
		{
			var guids = AssetDatabase.FindAssets("t:ItemDatabase");
			if (guids.Length == 0)
			{
				Debug.LogWarning("[HappyHub] No ItemDatabase asset found.");
				return;
			}
			var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]));
			EditorGUIUtility.PingObject(asset);
			Selection.activeObject = asset;
		}
	}
}
