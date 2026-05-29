using UnityEditor;
using UnityEngine;

namespace Starter.Shooter.EditorTools
{
	/// <summary>
	/// One-shot migration from the old inline tuning (HeldWeapon.Range/Damage/...,
	/// TrainingDummy.PunchRange/..., Player.Punch*) to the new CombatAction SO model.
	/// Creates the SO assets with the values the prefabs used pre-refactor, adds
	/// ActionInvoker components to Player + dummy prefabs, and wires the SOs into
	/// the appropriate Actions lists. Re-running is idempotent.
	/// </summary>
	public static class CombatActionMigration
	{
		private const string ActionsFolder = "Assets/03_Shooter/Actions";
		private const string PlayerPrefab = "Assets/03_Shooter/Prefabs/Player.prefab";
		private const string PistolPrefab = "Assets/03_Shooter/Prefabs/Hand_Pistol.prefab";
		private const string BatPrefab = "Assets/03_Shooter/Prefabs/Hand_Bat.prefab";
		private const string DummyPrefab = "Assets/03_Shooter/Prefabs/TrainingDummy.prefab";
		private const string MegaDummyPrefab = "Assets/03_Shooter/Prefabs/MegaTrainingDummy.prefab";
		private const string PassiveDummyPrefab = "Assets/03_Shooter/Prefabs/PassiveTrainingDummy.prefab";

		// Audio clips referenced by the original prefabs — resolved via GUID so the script
		// keeps working even if the clip files are moved.
		private const string PistolClipGuid = "4bf4f7f783a5af94fbc3dce27a76d5f9";
		private const string BatClipGuid = "8f89cd87b391d3d41b31a7133da3e383";

		[MenuItem("Combat/Migrate To CombatAction SOs")]
		public static void Migrate()
		{
			EnsureFolder(ActionsFolder);

			var pistol = CreateOrLoad<HitscanAction>($"{ActionsFolder}/Action_Pistol.asset", a =>
			{
				a.Damage = 1;
				a.Cooldown = 0.5f;
				a.KnockbackDistance = 2f;
				a.Range = 200f;
				a.Style = EFeedbackStyle.Ranged;
				a.AttackClip = LoadByGuid<AudioClip>(PistolClipGuid);
				a.AttackVolume = 1f;
				a.Charge.Enabled = false;
			});

			var bat = CreateOrLoad<OverlapAction>($"{ActionsFolder}/Action_Bat.asset", a =>
			{
				a.Damage = 2;
				a.Cooldown = 0.5f;
				a.KnockbackDistance = 2f;
				a.Range = 2f;
				a.SingleTarget = true;
				a.Style = EFeedbackStyle.Melee;
				a.AttackClip = LoadByGuid<AudioClip>(BatClipGuid);
				a.AttackVolume = 1f;
				// Bat keeps the existing melee charge behavior.
				a.Charge.Enabled = true;
				a.Charge.ThresholdSeconds = 2f;
				a.Charge.DamageMultiplier = 2f;
				a.Charge.KnockbackMultiplier = 2f;
			});

			var dummyPunch = CreateOrLoad<OverlapAction>($"{ActionsFolder}/Action_DummyPunch.asset", a =>
			{
				a.Damage = 1;
				a.Cooldown = 1.5f;
				a.KnockbackDistance = 0.5f;
				a.Range = 2.5f;
				a.SingleTarget = true;
				a.Style = EFeedbackStyle.Melee;
				a.Charge.Enabled = false;
			});

			var megaPunch = CreateOrLoad<OverlapAction>($"{ActionsFolder}/Action_MegaDummyPunch.asset", a =>
			{
				a.Damage = 2;
				a.Cooldown = 1.5f;
				a.KnockbackDistance = 4f;
				a.Range = 3.5f;
				a.SingleTarget = true;
				a.Style = EFeedbackStyle.Melee;
				a.Charge.Enabled = false;
			});

			// Wire actions into prefabs.
			WireHeldWeapon(PistolPrefab, pistol);
			WireHeldWeapon(BatPrefab, bat);

			EnsureActionInvoker(PlayerPrefab);
			WireDummy(DummyPrefab, dummyPunch);
			WireDummy(MegaDummyPrefab, megaPunch);
			// Passive dummy doesn't attack; still needs the [RequireComponent] ActionInvoker, but no action assignment.
			EnsureActionInvoker(PassiveDummyPrefab);

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log("[CombatActionMigration] Done. Created SOs under " + ActionsFolder + " and wired prefabs.");
		}

		private static T CreateOrLoad<T>(string path, System.Action<T> configure) where T : ScriptableObject
		{
			var existing = AssetDatabase.LoadAssetAtPath<T>(path);
			if (existing != null)
			{
				configure(existing);
				EditorUtility.SetDirty(existing);
				return existing;
			}
			var so = ScriptableObject.CreateInstance<T>();
			configure(so);
			AssetDatabase.CreateAsset(so, path);
			return so;
		}

		private static T LoadByGuid<T>(string guid) where T : Object
		{
			string path = AssetDatabase.GUIDToAssetPath(guid);
			if (string.IsNullOrEmpty(path)) return null;
			return AssetDatabase.LoadAssetAtPath<T>(path);
		}

		private static void EnsureFolder(string folderPath)
		{
			if (AssetDatabase.IsValidFolder(folderPath)) return;
			string parent = System.IO.Path.GetDirectoryName(folderPath).Replace('\\', '/');
			string leaf = System.IO.Path.GetFileName(folderPath);
			AssetDatabase.CreateFolder(parent, leaf);
		}

		private static void WireHeldWeapon(string prefabPath, CombatAction action)
		{
			var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
			if (prefab == null) { Debug.LogWarning($"[CombatActionMigration] Prefab not found: {prefabPath}"); return; }

			var weapon = prefab.GetComponentInChildren<HeldWeapon>(true);
			if (weapon == null) { Debug.LogWarning($"[CombatActionMigration] No HeldWeapon on {prefabPath}"); PrefabUtility.UnloadPrefabContents(prefab); return; }

			SetActionsList(weapon, action);

			PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
			PrefabUtility.UnloadPrefabContents(prefab);
		}

		private static void WireDummy(string prefabPath, CombatAction action)
		{
			var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
			if (prefab == null) { Debug.LogWarning($"[CombatActionMigration] Prefab not found: {prefabPath}"); return; }

			var dummy = prefab.GetComponentInChildren<TrainingDummy>(true);
			if (dummy == null) { Debug.LogWarning($"[CombatActionMigration] No TrainingDummy on {prefabPath}"); PrefabUtility.UnloadPrefabContents(prefab); return; }

			if (dummy.GetComponent<ActionInvoker>() == null)
			{
				dummy.gameObject.AddComponent<ActionInvoker>();
			}

			SetActionsList(dummy, action);

			PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
			PrefabUtility.UnloadPrefabContents(prefab);
		}

		private static void EnsureActionInvoker(string prefabPath)
		{
			var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
			if (prefab == null) { Debug.LogWarning($"[CombatActionMigration] Prefab not found: {prefabPath}"); return; }

			if (prefab.GetComponent<ActionInvoker>() == null)
			{
				prefab.AddComponent<ActionInvoker>();
			}

			PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
			PrefabUtility.UnloadPrefabContents(prefab);
		}

		// Sets a private [SerializeField] List<CombatAction> _actions on the given component.
		// Done via SerializedObject so Unity sees the change as a dirty serialized field.
		private static void SetActionsList(Object target, CombatAction action)
		{
			var so = new SerializedObject(target);
			var prop = so.FindProperty("_actions");
			if (prop == null || prop.isArray == false)
			{
				Debug.LogWarning($"[CombatActionMigration] _actions field not found on {target}");
				return;
			}
			prop.arraySize = 1;
			prop.GetArrayElementAtIndex(0).objectReferenceValue = action;
			so.ApplyModifiedPropertiesWithoutUndo();
		}
	}
}
