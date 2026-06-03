using System.IO;
using Starter.Common.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Starter.Common.EditorTools
{
	/// Editor window for rendering a prefab to a PNG icon. Spawns the prefab into a
	/// preview scene, frames it with an auto-positioned camera, saves the PNG, and
	/// shows the result in the window.
	public class PrefabScreenshotWindow : EditorWindow
	{
		private const string DefaultSaveFolder = "Assets/Screenshots";

		[SerializeField] private GameObject _prefab;
		[SerializeField] private ItemDefinition _itemDefinition;
		[SerializeField] private int _width = 256;
		[SerializeField] private int _height = 256;
		[SerializeField] private Color _backgroundColor = Color.white;
		[SerializeField] private bool _transparentBackground;
		[SerializeField] private string _saveFolder = DefaultSaveFolder;
		[SerializeField] private Vector3 _cameraEuler = new Vector3(20f, 135f, 0f);
		[SerializeField] private float _fieldOfView = 30f;
		[SerializeField, Range(0.1f, 1f)] private float _fillRatio = 0.8f;

		private Texture2D _preview;
		private string _previewPrefabName;
		private string _lastSavedPath;

		[MenuItem("Tools/Prefab Screenshot")]
		public static void Open()
		{
			var window = GetWindow<PrefabScreenshotWindow>("Prefab Screenshot");
			window.minSize = new Vector2(320f, 480f);
		}

		private void OnGUI()
		{
			EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
			_prefab = (GameObject)EditorGUILayout.ObjectField("Prefab", _prefab, typeof(GameObject), false);
			_itemDefinition = (ItemDefinition)EditorGUILayout.ObjectField("Item Definition", _itemDefinition, typeof(ItemDefinition), false);
			if (_itemDefinition != null)
				EditorGUILayout.HelpBox("Save will import the screenshot as a Sprite and assign it to this item's Icon.", MessageType.Info);

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
			_width = Mathf.Max(16, EditorGUILayout.IntField("Width", _width));
			_height = Mathf.Max(16, EditorGUILayout.IntField("Height", _height));
			_transparentBackground = EditorGUILayout.Toggle("Transparent Background", _transparentBackground);
			using (new EditorGUI.DisabledScope(_transparentBackground))
				_backgroundColor = EditorGUILayout.ColorField("Background", _backgroundColor);

			using (new EditorGUILayout.HorizontalScope())
			{
				_saveFolder = EditorGUILayout.TextField("Save Folder", _saveFolder);
				if (GUILayout.Button("...", GUILayout.Width(28f)))
				{
					var start = Directory.Exists(_saveFolder) ? _saveFolder : "Assets";
					var picked = EditorUtility.OpenFolderPanel("Choose Save Folder", start, "");
					if (!string.IsNullOrEmpty(picked))
					{
						var rel = ToProjectRelative(picked);
						if (rel != null)
							_saveFolder = rel;
						else
							EditorUtility.DisplayDialog("Invalid Folder", "Save folder must live inside the project's Assets/ directory.", "OK");
					}
				}
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Camera", EditorStyles.boldLabel);
			_cameraEuler = EditorGUILayout.Vector3Field("Rotation (Euler)", _cameraEuler);
			_fieldOfView = EditorGUILayout.Slider("Field Of View", _fieldOfView, 5f, 90f);
			_fillRatio = EditorGUILayout.Slider("Fill Ratio", _fillRatio, 0.1f, 1f);

			EditorGUILayout.Space();
			using (new EditorGUILayout.HorizontalScope())
			{
				using (new EditorGUI.DisabledScope(_prefab == null))
				{
					if (GUILayout.Button("Take Screenshot", GUILayout.Height(32f)))
						TakeScreenshot();
				}
				using (new EditorGUI.DisabledScope(_preview == null))
				{
					if (GUILayout.Button("Save", GUILayout.Height(32f)))
						SavePreview();
				}
			}

			if (_preview != null)
			{
				EditorGUILayout.Space();
				EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
				var rect = GUILayoutUtility.GetAspectRect((float)_width / Mathf.Max(1, _height));
				EditorGUI.DrawPreviewTexture(rect, _preview);
				if (!string.IsNullOrEmpty(_lastSavedPath))
				{
					EditorGUILayout.LabelField("Saved", _lastSavedPath);
					using (new EditorGUILayout.HorizontalScope())
					{
						if (GUILayout.Button("Ping In Project"))
						{
							var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(_lastSavedPath);
							if (asset != null)
								EditorGUIUtility.PingObject(asset);
						}
						if (GUILayout.Button("Reveal In Explorer"))
							EditorUtility.RevealInFinder(_lastSavedPath);
					}
				}
			}
		}

		private void TakeScreenshot()
		{
			var previewScene = EditorSceneManager.NewPreviewScene();
			GameObject instance = null;
			GameObject cameraGo = null;
			RenderTexture renderTexture = null;
			try
			{
				instance = (GameObject)PrefabUtility.InstantiatePrefab(_prefab, previewScene);
				if (instance == null)
				{
					Debug.LogError($"[PrefabScreenshot] Could not instantiate prefab '{_prefab.name}'.");
					return;
				}
				instance.transform.position = Vector3.zero;
				instance.transform.rotation = Quaternion.identity;

				if (!TryGetRenderBounds(instance, out var bounds))
				{
					Debug.LogWarning($"[PrefabScreenshot] Prefab '{_prefab.name}' has no renderers to frame.");
					return;
				}

				// Preview scenes can't take ambient via RenderSettings, so fake it with a
				// ring of directional lights covering all sides — no harsh single-source shadows.
				CreateFillLight(previewScene, "KeyLight",     Quaternion.Euler( 40f,  -35f, 0f), 1.0f);
				CreateFillLight(previewScene, "FillLight",    Quaternion.Euler( 15f,  145f, 0f), 0.7f);
				CreateFillLight(previewScene, "SideLeft",     Quaternion.Euler( 10f,  -90f, 0f), 0.5f);
				CreateFillLight(previewScene, "SideRight",    Quaternion.Euler( 10f,   90f, 0f), 0.5f);
				CreateFillLight(previewScene, "TopLight",     Quaternion.Euler( 80f,    0f, 0f), 0.45f);
				CreateFillLight(previewScene, "BottomUplight", Quaternion.Euler(-50f,   20f, 0f), 0.35f);

				cameraGo = new GameObject("ScreenshotCamera");
				SceneManager.MoveGameObjectToScene(cameraGo, previewScene);
				var camera = cameraGo.AddComponent<Camera>();
				camera.scene = previewScene;
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.fieldOfView = _fieldOfView;
				camera.nearClipPlane = 0.01f;
				camera.allowHDR = false;
				camera.allowMSAA = true;

				var rotation = Quaternion.Euler(_cameraEuler);
				cameraGo.transform.rotation = rotation;

				var radius = Mathf.Max(bounds.extents.magnitude, 0.001f);
				var halfFovRad = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
				var halfFovHorizontalRad = Mathf.Atan(Mathf.Tan(halfFovRad) * ((float)_width / Mathf.Max(1, _height)));
				var minHalfFov = Mathf.Min(halfFovRad, halfFovHorizontalRad);
				var distance = radius / Mathf.Sin(minHalfFov) / Mathf.Max(0.01f, _fillRatio);
				cameraGo.transform.position = bounds.center - rotation * Vector3.forward * distance;
				camera.farClipPlane = distance + radius * 4f;

				renderTexture = new RenderTexture(_width, _height, 24, RenderTextureFormat.ARGB32)
				{
					antiAliasing = 8,
				};
				camera.targetTexture = renderTexture;

				Color[] pixels;
				if (_transparentBackground)
				{
					// URP doesn't preserve alpha for solid-color clears, so render twice over
					// black and white and derive alpha from the difference.
					var black = RenderToPixels(camera, renderTexture, Color.black);
					var white = RenderToPixels(camera, renderTexture, Color.white);
					pixels = new Color[black.Length];
					for (var i = 0; i < pixels.Length; i++)
					{
						var b = black[i];
						var w = white[i];
						var dR = Mathf.Clamp01(w.r - b.r);
						var dG = Mathf.Clamp01(w.g - b.g);
						var dB = Mathf.Clamp01(w.b - b.b);
						var a = 1f - (dR + dG + dB) / 3f;
						if (a > 0.001f)
							pixels[i] = new Color(b.r / a, b.g / a, b.b / a, a);
						else
							pixels[i] = new Color(0f, 0f, 0f, 0f);
					}
				}
				else
				{
					pixels = RenderToPixels(camera, renderTexture, _backgroundColor);
				}

				var tex = new Texture2D(_width, _height, TextureFormat.RGBA32, false);
				tex.SetPixels(pixels);
				tex.Apply();

				if (_preview != null)
					DestroyImmediate(_preview);
				_preview = tex;
				_previewPrefabName = _prefab.name;
				_lastSavedPath = null;
				Repaint();
			}
			finally
			{
				if (cameraGo != null)
				{
					var cam = cameraGo.GetComponent<Camera>();
					if (cam != null)
						cam.targetTexture = null;
				}
				if (renderTexture != null)
				{
					if (RenderTexture.active == renderTexture)
						RenderTexture.active = null;
					renderTexture.Release();
					DestroyImmediate(renderTexture);
				}
				EditorSceneManager.ClosePreviewScene(previewScene);
			}
		}

		private Color[] RenderToPixels(Camera camera, RenderTexture rt, Color background)
		{
			camera.backgroundColor = background;
			camera.Render();
			var previousActive = RenderTexture.active;
			RenderTexture.active = rt;
			var tmp = new Texture2D(_width, _height, TextureFormat.RGBA32, false);
			tmp.ReadPixels(new Rect(0f, 0f, _width, _height), 0, 0);
			tmp.Apply();
			RenderTexture.active = previousActive;
			var pixels = tmp.GetPixels();
			DestroyImmediate(tmp);
			return pixels;
		}

		private void SavePreview()
		{
			if (_preview == null)
				return;

			Directory.CreateDirectory(_saveFolder);
			var baseName = _itemDefinition != null
				? _itemDefinition.name
				: (string.IsNullOrEmpty(_previewPrefabName) ? "Screenshot" : _previewPrefabName);
			var fileName = $"{baseName}_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
			var path = Path.Combine(_saveFolder, fileName).Replace('\\', '/');
			File.WriteAllBytes(path, _preview.EncodeToPNG());
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
			_lastSavedPath = path;

			if (_itemDefinition != null)
				AssignSpriteToItem(path);

			Repaint();
		}

		private void AssignSpriteToItem(string path)
		{
			// Re-import as a Sprite so it can be referenced by ItemDefinition.Icon. The PNG was
			// just imported synchronously, so the importer is guaranteed available here.
			var importer = AssetImporter.GetAtPath(path) as TextureImporter;
			if (importer == null)
			{
				Debug.LogWarning($"[PrefabScreenshot] No TextureImporter at '{path}'; cannot assign Icon to '{_itemDefinition.name}'.");
				return;
			}

			importer.textureType = TextureImporterType.Sprite;
			importer.spriteImportMode = SpriteImportMode.Single;
			importer.alphaIsTransparency = _transparentBackground;
			importer.SaveAndReimport();

			var sprite = LoadSpriteAtPath(path);
			if (sprite == null)
			{
				Debug.LogWarning($"[PrefabScreenshot] Saved '{path}' but could not load it as a Sprite to assign to '{_itemDefinition.name}'.");
				return;
			}

			Undo.RecordObject(_itemDefinition, "Assign Item Icon");
			_itemDefinition.Icon = sprite;
			EditorUtility.SetDirty(_itemDefinition);
			AssetDatabase.SaveAssetIfDirty(_itemDefinition);
		}

		private static Sprite LoadSpriteAtPath(string path)
		{
			var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
			if (sprite != null)
				return sprite;

			// Fall back to scanning sub-assets — the main asset is the Texture2D.
			foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
			{
				if (asset is Sprite found)
					return found;
			}
			return null;
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
				if (renderer is ParticleSystemRenderer)
					continue;
				if (!initialized)
				{
					bounds = renderer.bounds;
					initialized = true;
				}
				else
				{
					bounds.Encapsulate(renderer.bounds);
				}
			}
			return initialized;
		}

		private static string ToProjectRelative(string absolutePath)
		{
			absolutePath = absolutePath.Replace('\\', '/');
			var dataPath = Application.dataPath.Replace('\\', '/');
			if (absolutePath == dataPath)
				return "Assets";
			if (absolutePath.StartsWith(dataPath + "/"))
				return "Assets" + absolutePath.Substring(dataPath.Length);
			return null;
		}
	}
}
