using System.IO;
using Starter.City;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace Starter.EditorTools
{
	/// <summary>
	/// Editor-side half of the static-city pipeline: build the deterministic city geometry in the open scene,
	/// bake the NavMesh against it, and persist the NavMesh data as an asset. The geometry itself is flagged
	/// DontSave — it is rebuilt identically at runtime from <see cref="StaticCity.CitySeed"/>, so only the
	/// baked NavMesh (small) ever hits disk.
	/// </summary>
	public static class StaticCityBaker
	{
		private const string MenuRoot = "Tools/HorrorTown/";

		[MenuItem(MenuRoot + "Generate City + Bake NavMesh")]
		public static void GenerateAndBake()
		{
			var city = FindCity();
			if (city == null) return;

			Debug.Log($"[StaticCityBaker] Building city (seed {city.CitySeed})…");
			city.Build(editorPreview: true);

			var surface = FindOrCreateSurface(city);

			// Bake from colliders, not render meshes: colliders are what the character actually walks on, and
			// the decorative road quads (no collider) then can't create phantom walkable islands.
			surface.collectObjects = CollectObjects.All;
			surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
			surface.layerMask = ~0;

			Debug.Log("[StaticCityBaker] Baking NavMesh — a full city takes a minute…");
			surface.BuildNavMesh();
			SaveNavMeshData(surface);

			EditorUtility.SetDirty(surface);
			EditorSceneManager.MarkSceneDirty(city.gameObject.scene);
			Debug.Log("[StaticCityBaker] Done. City preview is DontSave; save the scene to keep the NavMesh reference.");
		}

		[MenuItem(MenuRoot + "Toggle City Preview")]
		public static void TogglePreview()
		{
			var city = FindCity();
			if (city == null) return;

			if (city.transform.Find(StaticCity.GeometryRootName) != null) city.Clear();
			else city.Build(editorPreview: true);
		}

		[MenuItem(MenuRoot + "Clear City Preview")]
		public static void ClearPreview() => FindCity()?.Clear();

		private static StaticCity FindCity()
		{
			var city = Object.FindAnyObjectByType<StaticCity>();
			if (city == null)
				Debug.LogError("[StaticCityBaker] No StaticCity component in the open scene. Add one to a scene object first.");
			return city;
		}

		private static NavMeshSurface FindOrCreateSurface(StaticCity city)
		{
			var surface = Object.FindAnyObjectByType<NavMeshSurface>();
			if (surface != null) return surface;

			var go = new GameObject("NavMesh");
			go.transform.SetParent(city.transform.parent, false);
			return go.AddComponent<NavMeshSurface>();
		}

		/// <summary>BuildNavMesh leaves the data in memory; without an asset the bake would silently vanish on
		/// the next domain reload. Saved next to the scene, replacing any previous bake.</summary>
		private static void SaveNavMeshData(NavMeshSurface surface)
		{
			var data = surface.navMeshData;
			if (data == null)
			{
				Debug.LogError("[StaticCityBaker] Bake produced no NavMesh data.");
				return;
			}

			if (EditorUtility.IsPersistent(data)) return; // already an asset, bake updated it in place

			var scene = surface.gameObject.scene;
			string dir = Path.Combine(Path.GetDirectoryName(scene.path) ?? "Assets", scene.name);
			if (!AssetDatabase.IsValidFolder(dir))
				AssetDatabase.CreateFolder(Path.GetDirectoryName(scene.path), scene.name);

			// CreateAsset over an existing path replaces the content but keeps the GUID — which is what keeps
			// the scene's NavMeshSurface reference valid across rebakes.
			string path = $"{dir}/NavMesh-{surface.gameObject.name}.asset";
			AssetDatabase.CreateAsset(data, path);
			AssetDatabase.SaveAssets();
			Debug.Log($"[StaticCityBaker] NavMesh data saved to {path}.");
		}
	}
}
