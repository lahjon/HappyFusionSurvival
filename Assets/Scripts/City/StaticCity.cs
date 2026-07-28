using UnityEngine;

namespace Starter.City
{
	/// <summary>
	/// The city as a fixed, bakeable environment. The layout and every building interior derive from one
	/// serialized <see cref="CitySeed"/> that never changes between runs — which is what lets the NavMesh be
	/// baked in the editor and shipped with the scene. What changes per run is the <em>content</em>: barriers,
	/// keys, terminals and monsters, all placed by <c>RunDirector</c> from the networked run seed.
	///
	/// <para>Geometry is deliberately never saved into the scene — a square kilometre of meshes would be a
	/// gigabyte of YAML. Instead it is rebuilt deterministically: the editor baker builds it to bake against
	/// (flagged DontSave), and <see cref="Awake"/> builds the identical geometry again at runtime. As long as
	/// the seed is untouched between bake and play, the baked NavMesh always matches the world.</para>
	///
	/// <para>Plain MonoBehaviour on purpose: every peer has the same scene and the same seed, so the city needs
	/// no networking at all.</para>
	/// </summary>
	[DefaultExecutionOrder(-500)]
	public sealed class StaticCity : MonoBehaviour
	{
		public const string GeometryRootName = "GeneratedCity";

		public static StaticCity Instance { get; private set; }

		[Tooltip("The one number the whole environment derives from. Changing it changes the city — rebake the " +
			"NavMesh afterwards or monsters will navigate a town that no longer exists.")]
		public int CitySeed = 20260728;

		[Tooltip("City tuning. Leave empty to use built-in defaults.")]
		[SerializeField] private CitySettings _settings;

		[Tooltip("Art kit. Leave empty to build from flat-tinted primitives.")]
		[SerializeField] private BuildingKit _kit;

		/// <summary>The generated layout, available after <see cref="EnsureBuilt"/> (runtime: from Awake).</summary>
		public CityLayout Layout { get; private set; }

		public BuildingKit Kit => _kit;

		private void Awake()
		{
			Instance = this;
			EnsureBuilt();
		}

		private void OnDestroy()
		{
			if (Instance == this) Instance = null;
		}

		/// <summary>Builds layout + full geometry if not already built this session. Idempotent.</summary>
		public void EnsureBuilt()
		{
			if (Layout != null && transform.Find(GeometryRootName) != null) return;
			Build(editorPreview: false);
		}

		/// <summary>
		/// Deterministically (re)builds the whole city under this transform: ground, roads and every building
		/// with its full interior. No streaming — the environment is static so the baked NavMesh stays valid,
		/// and a city of flat-shaded boxes is well within budget even at full extent.
		/// </summary>
		public GameObject Build(bool editorPreview)
		{
			Clear();

			Layout = CityLayoutGenerator.Generate(CitySeed, _settings);

			var root = new GameObject(GeometryRootName);
			root.transform.SetParent(transform, false);

			CityBuilder.BuildGround(Layout, _kit, root.transform);

			// Street lighting is part of the static environment: posts everywhere, but only the lights near the
			// local camera ever run (StreetLightGrid culls them). Alleys stay dark by design.
			var lightRoot = new GameObject("StreetLights");
			lightRoot.transform.SetParent(root.transform, false);
			var grid = lightRoot.AddComponent<StreetLightGrid>();
			grid.Build(Layout, _settings, _kit != null ? _kit.StreetLight : null, CitySeed);

			var buildings = new GameObject("Buildings").transform;
			buildings.SetParent(root.transform, false);

			for (int i = 0; i < Layout.BuildingLots.Count; i++)
			{
				var lot = Layout.Lots[Layout.BuildingLots[i]];
				CityBuilder.BuildInterior(lot, _kit, buildings, CitySeed);
			}

			if (editorPreview)
				ApplyDontSaveRecursive(root.transform);

			return root;
		}

		/// <summary>Destroys any previously generated geometry (including an editor preview left in the scene).</summary>
		public void Clear()
		{
			Layout = null;
			for (int i = transform.childCount - 1; i >= 0; i--)
			{
				var child = transform.GetChild(i);
				if (child.name != GeometryRootName) continue;
				if (Application.isPlaying) Destroy(child.gameObject);
				else DestroyImmediate(child.gameObject);
			}
		}

		/// <summary>Editor previews must never be serialized into the scene file — the flag has to sit on every
		/// object, Unity does not inherit it from the parent.</summary>
		private static void ApplyDontSaveRecursive(Transform t)
		{
			t.gameObject.hideFlags = HideFlags.DontSave;
			for (int i = 0; i < t.childCount; i++)
				ApplyDontSaveRecursive(t.GetChild(i));
		}
	}
}
