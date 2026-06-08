using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace Starter.Common.Inventory
{
	[System.Serializable]
	public struct ItemStat
	{
		public string Label;
		public string Value;
	}

	[System.Serializable]
	public struct ItemWorldPhysics
	{
		[Tooltip("ON: when dropped in a WaterVolume, this item floats to the surface and self-rights " +
		         "(e.g. a sealed case, a cork). OFF (default): it sinks. Read by the generic pickup; bespoke " +
		         "WorldPrefab items use this too via PickupableItem.")]
		public bool Floats;

		[Tooltip("ON: override the dropped item's rigidbody center of mass with the offset below. A low COM " +
		         "(negative Y) makes the item bottom-heavy so it self-rights and floats the right way up. " +
		         "Only relevant when Floats is on — a sinking item keeps Unity's auto-computed COM.")]
		public bool OverrideCenterOfMass;

		[Tooltip("Local-space center of mass when the override is on.")]
		public Vector3 CenterOfMass;
	}

	[CreateAssetMenu(fileName = "Item", menuName = "Inventory/Item Data", order = 0)]
	public class ItemData : ScriptableObject
	{
		[Tooltip("Stable network id. Must be non-zero and unique within the ItemDatabase.")]
#if UNITY_EDITOR
		[InlineButton(nameof(RemoveFromDatabaseEditorOnly), "Remove from Database")]
		[InlineButton(nameof(AddToDatabaseEditorOnly), "Add to Database")]
#endif
		public short Id = 1;

		public string DisplayName = "Item";

		[InlineButton(nameof(GenerateIconEditorOnly), "Generate Icon")]
		public Sprite Icon;

		[TextArea(2, 6)]
		public string Description;

		[Tooltip("Optional list of stat rows shown in the tooltip (e.g. Damage / 12).")]
		public ItemStat[] Stats;

		[Header("Setup")]
		[Tooltip("Visual-only prefab instantiated as this item's model on the shared world pickup and hand " +
		         "rig (Pickup_Generic / Hand_Generic). The single source of an ordinary item's look — no " +
		         "gameplay components. Leave null to fall back to the generic rig's placeholder primitive.")]
#if UNITY_EDITOR
		[InlineButton(nameof(GeneratePrefabEditorOnly), "Generate")]
#endif
		public GameObject Prefab;

		[Tooltip("ON: picking this item up runs every ConsumableCapability's effect immediately and skips " +
		         "the inventory entirely (the pickup is consumed/despawned on the spot).")]
		public bool ConsumeOnPickup = false;

		[Tooltip("Optional sound played locally on the player when this item is picked up.")]
		public AudioClip PickupAudio;

		[Tooltip("Optional sound played locally on the player when this item is used from the inventory " +
		         "(e.g. drinking a potion, eating food for its healing effect).")]
		public AudioClip OnUseAudio;

		[Tooltip("Maximum items per inventory slot. 1 = not stackable (each pickup creates a new slot). >1 = stackable up to this count; overflow spills into the next slot.")]
		[Min(1)]
		public int MaxStack = 1;

		[Tooltip("Weight per unit. Total inventory weight reduces movement speed above the player's WeightLimit.")]
		[Min(0f)]
		public float Weight = 0f;

		[Tooltip("Scraps granted when ONE unit of this item is scavenged at a crafting bench. The item is destroyed in exchange for this much of the generic 'scraps' crafting currency.")]
		[Min(0)]
		public int ScrapValue = 1;

		[Tooltip("Base economic value (money). A procedurally-stocked shop sets its buy price to BaseValue " +
		         "scaled by the shop's markup; the sell price is a percentage of that buy price.")]
		[Min(0)]
		public int BaseValue = 10;

		[Tooltip("Scale/offset/flair the shared generic prefabs use to position this item's model " +
		         "(see Prefab above) on the world pickup and in-hand rig, plus optional bespoke " +
		         "WorldPrefab/HandPrefab overrides.")]
		public ItemVisual Visual = new();

		[Tooltip("Floating / center-of-mass tuning for this item's dropped-in-world rigidbody. Collapsed " +
		         "by default — expand only when this item needs custom water/buoyancy behaviour.")]
		public ItemWorldPhysics WorldPhysics;

		[Tooltip("Composable behavior modules. Add a WeaponCapability to make this item a weapon, a " +
		         "PlaceableCapability to make it placeable, a ConsumableCapability to make it usable, etc. " +
		         "Facets compose freely on one item.")]
		[SerializeReference]
		public List<ItemCapability> Capabilities = new();

		/// <summary>First capability of type <typeparamref name="T"/> on this item, or null.</summary>
		public T GetCapability<T>() where T : ItemCapability
		{
			if (Capabilities == null) return null;
			for (int i = 0; i < Capabilities.Count; i++)
			{
				if (Capabilities[i] is T match) return match;
			}
			return null;
		}

		public bool TryGetCapability<T>(out T capability) where T : ItemCapability
		{
			capability = GetCapability<T>();
			return capability != null;
		}

		public bool HasCapability<T>() where T : ItemCapability => GetCapability<T>() != null;

		/// <summary>
		/// The <c>InventorySlot.Loaded</c> value a fresh unit of this item should enter a slot with — the max
		/// <see cref="ItemCapability.InitialLoaded"/> across its capabilities (e.g. a gadget's charge count).
		/// 0 for ordinary items. Used by <c>InventoryOps.TryAdd</c> when filling an empty slot.
		/// </summary>
		public short InitialLoaded()
		{
			if (Capabilities == null) return 0;
			short max = 0;
			for (int i = 0; i < Capabilities.Count; i++)
			{
				if (Capabilities[i] == null) continue;
				short v = Capabilities[i].InitialLoaded;
				if (v > max) max = v;
			}
			return max;
		}

#if UNITY_EDITOR
		// ── Editor-only "Add to Database" (Odin InlineButton on Id above) ───────────
		// Finds the project's ItemDatabase asset (same lookup as ItemDatabaseValidator), adds this
		// item if it isn't already listed, and assigns it the smallest free positive id.
		private void AddToDatabaseEditorOnly()
		{
			var dbGuids = AssetDatabase.FindAssets("t:" + nameof(ItemDatabase));
			if (dbGuids.Length == 0)
			{
				Debug.LogError("[ItemData] No ItemDatabase asset found.");
				return;
			}

			var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(AssetDatabase.GUIDToAssetPath(dbGuids[0]));
			var so = new SerializedObject(db);
			var itemsProp = so.FindProperty("_items");

			for (int i = 0; i < itemsProp.arraySize; i++)
			{
				if (itemsProp.GetArrayElementAtIndex(i).objectReferenceValue == this)
				{
					Debug.Log($"[ItemData] '{name}' is already in '{db.name}'.");
					return;
				}
			}

			var used = new HashSet<short>();
			foreach (var existing in db.All)
				if (existing != null && existing.Id != 0) used.Add(existing.Id);

			if (Id == 0 || used.Contains(Id))
			{
				short next = 1;
				while (used.Contains(next)) next++;
				Undo.RecordObject(this, "Assign Item Id");
				Id = next;
				EditorUtility.SetDirty(this);
			}

			Undo.RecordObject(db, "Add Item to Database");
			itemsProp.arraySize++;
			itemsProp.GetArrayElementAtIndex(itemsProp.arraySize - 1).objectReferenceValue = this;
			so.ApplyModifiedProperties();
			EditorUtility.SetDirty(db);
			AssetDatabase.SaveAssets();

			Debug.Log($"[ItemData] Added '{name}' to '{db.name}' with Id {Id}.");
		}

		// ── Editor-only "Generate" prefab (Odin InlineButton on Prefab above) ───────
		// Creates a prefab variant of the shared _PickupableItem template (see RedPotion.prefab for
		// the convention this mirrors), named after this item's DisplayName, wires this ItemData as
		// the variant's _initialItem, and instantiates this item's visual Prefab as a child of the
		// template's _visualRoot — the same shape RedPotion.prefab has.
		private const string PickupableItemTemplatePath = "Assets/Items/Prefabs/_PickupableItem.prefab";
		private const string GeneratedPrefabFolder = "Assets/Items/Prefabs";

		private void GeneratePrefabEditorOnly()
		{
			var template = AssetDatabase.LoadAssetAtPath<GameObject>(PickupableItemTemplatePath);
			if (template == null)
			{
				Debug.LogError($"[ItemData] Template prefab not found at '{PickupableItemTemplatePath}'.");
				return;
			}

			Directory.CreateDirectory(GeneratedPrefabFolder);
			var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{GeneratedPrefabFolder}/{DisplayName}.prefab");

			var instance = (GameObject)PrefabUtility.InstantiatePrefab(template);
			try
			{
				instance.name = DisplayName;

				var pickup = instance.GetComponentInChildren<PickupableItem>(true);
				if (pickup == null)
				{
					Debug.LogWarning($"[ItemData] Template '{template.name}' has no PickupableItem component.");
				}
				else
				{
					var so = new SerializedObject(pickup);
					var initialItemProp = so.FindProperty("_initialItem");
					var visualRootProp = so.FindProperty("_visualRoot");
					if (initialItemProp != null) initialItemProp.objectReferenceValue = this;
					so.ApplyModifiedProperties();

					if (Prefab != null)
					{
						var visualRoot = visualRootProp != null ? visualRootProp.objectReferenceValue as Transform : null;
						if (visualRoot != null)
							PrefabUtility.InstantiatePrefab(Prefab, visualRoot);
						else
							Debug.LogWarning($"[ItemData] '{template.name}' has no _visualRoot; '{Prefab.name}' was not parented.");
					}
				}

				var saved = PrefabUtility.SaveAsPrefabAsset(instance, assetPath);
				Selection.activeObject = saved;
				Debug.Log($"[ItemData] Generated '{assetPath}' from '{template.name}'.");
			}
			finally
			{
				DestroyImmediate(instance);
			}
		}

		// ── Editor-only "Remove from Database" (Odin InlineButton on Id above) ──────
		// Removes this item from the project's ItemDatabase asset, if present. Leaves Id untouched.
		private void RemoveFromDatabaseEditorOnly()
		{
			var dbGuids = AssetDatabase.FindAssets("t:" + nameof(ItemDatabase));
			if (dbGuids.Length == 0)
			{
				Debug.LogError("[ItemData] No ItemDatabase asset found.");
				return;
			}

			var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(AssetDatabase.GUIDToAssetPath(dbGuids[0]));
			var so = new SerializedObject(db);
			var itemsProp = so.FindProperty("_items");

			for (int i = 0; i < itemsProp.arraySize; i++)
			{
				if (itemsProp.GetArrayElementAtIndex(i).objectReferenceValue == this)
				{
					Undo.RecordObject(db, "Remove Item from Database");
					itemsProp.DeleteArrayElementAtIndex(i);
					so.ApplyModifiedProperties();
					EditorUtility.SetDirty(db);
					AssetDatabase.SaveAssets();
					Debug.Log($"[ItemData] Removed '{name}' from '{db.name}'.");
					return;
				}
			}

			Debug.Log($"[ItemData] '{name}' is not in '{db.name}'.");
		}

		// ── Editor-only icon generation (Odin InlineButton on Icon above) ──────────
		// Renders this item's Prefab (or a bespoke Visual.WorldPrefab) to a transparent-background
		// PNG using the same render-to-texture approach as Tools/Prefab Screenshot
		// (Assets/Common/Editor/PrefabScreenshotWindow.cs), imports it as a Sprite, and assigns it
		// to Icon. #if-gated so none of this (or the UnityEditor reference) ships in builds.
		private const string IconSaveFolder = "Assets/Items/Icons";
		private const int IconSize = 256;

		private void GenerateIconEditorOnly()
		{
			GameObject prefab = Prefab != null ? Prefab : (Visual != null ? Visual.WorldPrefab : null);
			if (prefab == null)
			{
				Debug.LogWarning($"[ItemData] '{name}' has no Prefab (or Visual.WorldPrefab) to render an icon from.");
				return;
			}

			var texture = RenderTransparentIcon(prefab, IconSize, IconSize);
			if (texture == null)
			{
				Debug.LogWarning($"[ItemData] Could not render an icon for '{name}' — '{prefab.name}' has no renderers to frame.");
				return;
			}

			Directory.CreateDirectory(IconSaveFolder);
			var fileName = $"{name}_Icon_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
			var path = Path.Combine(IconSaveFolder, fileName).Replace('\\', '/');
			File.WriteAllBytes(path, texture.EncodeToPNG());
			DestroyImmediate(texture);
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

			var importer = AssetImporter.GetAtPath(path) as TextureImporter;
			if (importer == null)
			{
				Debug.LogWarning($"[ItemData] No TextureImporter at '{path}'; cannot assign Icon to '{name}'.");
				return;
			}

			importer.textureType = TextureImporterType.Sprite;
			importer.spriteImportMode = SpriteImportMode.Single;
			importer.alphaIsTransparency = true;
			importer.SaveAndReimport();

			Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
			if (sprite == null)
			{
				foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
				{
					if (asset is Sprite found) { sprite = found; break; }
				}
			}
			if (sprite == null)
			{
				Debug.LogWarning($"[ItemData] Saved '{path}' but could not load it as a Sprite to assign to '{name}'.");
				return;
			}

			Undo.RecordObject(this, "Generate Item Icon");
			Icon = sprite;
			EditorUtility.SetDirty(this);
			AssetDatabase.SaveAssetIfDirty(this);
		}

		private static Texture2D RenderTransparentIcon(GameObject prefab, int width, int height)
		{
			var cameraEuler = new Vector3(20f, 135f, 0f);
			const float fieldOfView = 30f;
			const float fillRatio = 0.8f;

			var previewScene = EditorSceneManager.NewPreviewScene();
			GameObject instance = null;
			GameObject cameraGo = null;
			RenderTexture renderTexture = null;
			try
			{
				instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, previewScene);
				if (instance == null) return null;

				instance.transform.position = Vector3.zero;
				instance.transform.rotation = Quaternion.identity;

				if (!TryGetRenderBounds(instance, out var bounds)) return null;

				CreateFillLight(previewScene, "KeyLight",      Quaternion.Euler( 40f,  -35f, 0f), 1.0f);
				CreateFillLight(previewScene, "FillLight",     Quaternion.Euler( 15f,  145f, 0f), 0.7f);
				CreateFillLight(previewScene, "SideLeft",      Quaternion.Euler( 10f,  -90f, 0f), 0.5f);
				CreateFillLight(previewScene, "SideRight",     Quaternion.Euler( 10f,   90f, 0f), 0.5f);
				CreateFillLight(previewScene, "TopLight",      Quaternion.Euler( 80f,    0f, 0f), 0.45f);
				CreateFillLight(previewScene, "BottomUplight", Quaternion.Euler(-50f,   20f, 0f), 0.35f);

				cameraGo = new GameObject("IconCamera");
				SceneManager.MoveGameObjectToScene(cameraGo, previewScene);
				var camera = cameraGo.AddComponent<Camera>();
				camera.scene = previewScene;
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.fieldOfView = fieldOfView;
				camera.nearClipPlane = 0.01f;
				camera.allowHDR = false;
				camera.allowMSAA = true;

				var rotation = Quaternion.Euler(cameraEuler);
				cameraGo.transform.rotation = rotation;

				var radius = Mathf.Max(bounds.extents.magnitude, 0.001f);
				var halfFovRad = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
				var halfFovHorizontalRad = Mathf.Atan(Mathf.Tan(halfFovRad) * ((float)width / Mathf.Max(1, height)));
				var minHalfFov = Mathf.Min(halfFovRad, halfFovHorizontalRad);
				var distance = radius / Mathf.Sin(minHalfFov) / Mathf.Max(0.01f, fillRatio);
				cameraGo.transform.position = bounds.center - rotation * Vector3.forward * distance;
				camera.farClipPlane = distance + radius * 4f;

				renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
				camera.targetTexture = renderTexture;

				var black = RenderToPixels(camera, renderTexture, Color.black, width, height);
				var white = RenderToPixels(camera, renderTexture, Color.white, width, height);
				var pixels = new Color[black.Length];
				for (var i = 0; i < pixels.Length; i++)
				{
					var b = black[i];
					var w = white[i];
					var dR = Mathf.Clamp01(w.r - b.r);
					var dG = Mathf.Clamp01(w.g - b.g);
					var dB = Mathf.Clamp01(w.b - b.b);
					var a = 1f - (dR + dG + dB) / 3f;
					pixels[i] = a > 0.001f ? new Color(b.r / a, b.g / a, b.b / a, a) : new Color(0f, 0f, 0f, 0f);
				}

				var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
				tex.SetPixels(pixels);
				tex.Apply();
				return tex;
			}
			finally
			{
				if (cameraGo != null)
				{
					var cam = cameraGo.GetComponent<Camera>();
					if (cam != null) cam.targetTexture = null;
				}
				if (renderTexture != null)
				{
					if (RenderTexture.active == renderTexture) RenderTexture.active = null;
					renderTexture.Release();
					DestroyImmediate(renderTexture);
				}
				EditorSceneManager.ClosePreviewScene(previewScene);
			}
		}

		private static Color[] RenderToPixels(Camera camera, RenderTexture rt, Color background, int width, int height)
		{
			camera.backgroundColor = background;
			camera.Render();
			var previousActive = RenderTexture.active;
			RenderTexture.active = rt;
			var tmp = new Texture2D(width, height, TextureFormat.RGBA32, false);
			tmp.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
			tmp.Apply();
			RenderTexture.active = previousActive;
			var pixels = tmp.GetPixels();
			DestroyImmediate(tmp);
			return pixels;
		}

		private static void CreateFillLight(Scene scene, string name, Quaternion rotation, float intensity)
		{
			var go = new GameObject(name);
			SceneManager.MoveGameObjectToScene(go, scene);
			go.transform.rotation = rotation;
			var light = go.AddComponent<Light>();
			light.type = LightType.Directional;
			light.intensity = intensity;
			light.color = Color.white;
			light.shadows = LightShadows.None;
		}

		private static bool TryGetRenderBounds(GameObject root, out Bounds bounds)
		{
			var renderers = root.GetComponentsInChildren<Renderer>();
			bounds = default;
			var initialized = false;
			foreach (var renderer in renderers)
			{
				if (renderer is ParticleSystemRenderer) continue;
				if (!initialized) { bounds = renderer.bounds; initialized = true; }
				else bounds.Encapsulate(renderer.bounds);
			}
			return initialized;
		}
#endif
	}
}
