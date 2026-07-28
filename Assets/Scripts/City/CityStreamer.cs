using System.Collections.Generic;
using UnityEngine;

namespace Starter.City
{
	/// <summary>
	/// Decides which buildings are real at any moment.
	///
	/// <para>A square kilometre with walk-in interiors cannot all exist at once — a thousand buildings times a
	/// dozen rooms times four floors is millions of triangles and hundreds of thousands of colliders. So every
	/// building is permanently present as a cheap solid shell (skyline, sightlines, walkable roof), and only
	/// buildings near a player are swapped for their walk-in form.</para>
	///
	/// <para>Streaming is driven by <i>observers</i> rather than by the local camera alone. On the host, every
	/// player is an observer: monsters and hit detection need real colliders wherever any player is, not just
	/// where this peer is looking. Interiors are built at most a couple per frame so walking into a dense block
	/// never spikes the frame time.</para>
	///
	/// <para>Nothing here is networked. Geometry is a pure function of the seed, so each peer streams
	/// independently and they never need to agree on what is loaded.</para>
	/// </summary>
	public sealed class CityStreamer : MonoBehaviour
	{
		[Header("Radii (metres)")]
		[Tooltip("Buildings within this distance of any observer get their walk-in interior built.")]
		[Min(20f)] public float InteriorRadius = 90f;

		[Tooltip("Interiors are torn down past this distance. The gap between the two radii is hysteresis — " +
			"without it, standing on the boundary rebuilds a building every frame.")]
		[Min(30f)] public float UnloadRadius = 140f;

		[Header("Budget")]
		[Tooltip("Interiors built per frame. Keep low — one building is a few thousand triangles and a mesh collider bake.")]
		[Min(1)] public int BuildsPerFrame = 2;

		[Tooltip("Interiors torn down per frame.")]
		[Min(1)] public int TeardownsPerFrame = 4;

		[Tooltip("Seconds between re-evaluating which buildings should be loaded. Players cannot cross the " +
			"hysteresis band faster than this, so there is no reason to scan every frame.")]
		[Min(0.05f)] public float ScanInterval = 0.25f;

		/// <summary>Raised after a building's interior is built, so content (barriers, props) can attach to it.</summary>
		public event System.Action<CityLot, GameObject> BuildingRealised;

		/// <summary>Raised just before a building's interior is destroyed.</summary>
		public event System.Action<CityLot, GameObject> BuildingReleased;

		private CityLayout _layout;
		private BuildingKit _kit;
		private int _seed;
		private Transform _shellRoot;
		private Transform _interiorRoot;

		private readonly List<Transform> _observers = new List<Transform>(8);
		private readonly Dictionary<int, GameObject> _shells = new Dictionary<int, GameObject>(1024);
		private readonly Dictionary<int, GameObject> _interiors = new Dictionary<int, GameObject>(64);
		private readonly List<int> _pendingBuild = new List<int>(32);
		private readonly List<int> _pendingRelease = new List<int>(32);
		private readonly List<CityLot> _scratch = new List<CityLot>(64);
		private readonly HashSet<int> _wanted = new HashSet<int>();

		private float _nextScan;
		private bool _ready;

		// =====================================================================

		/// <summary>Build the always-on parts of the city (ground, roads, street lighting, every building shell)
		/// and start streaming interiors.</summary>
		public void Initialize(CityLayout layout, BuildingKit kit, CitySettings settings, int seed)
		{
			_layout = layout;
			_kit = kit;
			_seed = seed;

			CityBuilder.BuildGround(layout, kit, transform);

			// Street lighting shares this component's observer list, so lamps light up around exactly the same
			// players that pull interiors in.
			if (kit != null && kit.StreetLight != null)
			{
				var lightRoot = new GameObject("StreetLights");
				lightRoot.transform.SetParent(transform, false);

				var grid = lightRoot.AddComponent<StreetLightGrid>();
				grid.Build(layout, settings, kit.StreetLight, seed);
				grid.SetObservers(_observers);
			}

			_shellRoot = new GameObject("Shells").transform;
			_shellRoot.SetParent(transform, false);

			_interiorRoot = new GameObject("Interiors").transform;
			_interiorRoot.SetParent(transform, false);

			for (int i = 0; i < layout.BuildingLots.Count; i++)
			{
				int lotIndex = layout.BuildingLots[i];
				var shell = CityBuilder.BuildShell(layout.Lots[lotIndex], kit, _shellRoot);
				if (shell != null) _shells[lotIndex] = shell;
			}

			_ready = true;
		}

		public void AddObserver(Transform observer)
		{
			if (observer == null || _observers.Contains(observer)) return;
			_observers.Add(observer);
		}

		public void RemoveObserver(Transform observer) => _observers.Remove(observer);

		// =====================================================================

		private void Update()
		{
			if (!_ready) return;

			if (Time.unscaledTime >= _nextScan)
			{
				_nextScan = Time.unscaledTime + ScanInterval;
				Scan();
			}

			ProcessQueues();
		}

		/// <summary>Works out which buildings should be walk-in right now and queues the difference.</summary>
		private void Scan()
		{
			PruneObservers();
			_wanted.Clear();

			for (int i = 0; i < _observers.Count; i++)
			{
				_layout.BuildingLotsNear(_observers[i].position, InteriorRadius, _scratch);
				for (int j = 0; j < _scratch.Count; j++)
					_wanted.Add(_scratch[j].Index);
			}

			foreach (int lotIndex in _wanted)
			{
				if (_interiors.ContainsKey(lotIndex)) continue;
				if (_pendingBuild.Contains(lotIndex)) continue;
				_pendingBuild.Add(lotIndex);
			}

			// Release anything that has fallen outside the outer radius from every observer.
			foreach (var pair in _interiors)
			{
				int lotIndex = pair.Key;
				if (_wanted.Contains(lotIndex)) continue;
				if (StillWithin(lotIndex, UnloadRadius)) continue;
				if (_pendingRelease.Contains(lotIndex)) continue;
				_pendingRelease.Add(lotIndex);
			}

			// A queued build that has already drifted away is cheaper to cancel than to build and tear down.
			for (int i = _pendingBuild.Count - 1; i >= 0; i--)
				if (!_wanted.Contains(_pendingBuild[i])) _pendingBuild.RemoveAt(i);
		}

		private bool StillWithin(int lotIndex, float radius)
		{
			var lot = _layout.Lots[lotIndex];
			float sqr = radius * radius;
			for (int i = 0; i < _observers.Count; i++)
			{
				var p = _observers[i].position;
				var c = lot.Center;
				float dx = p.x - c.x, dz = p.z - c.z;
				if (dx * dx + dz * dz <= sqr) return true;
			}
			return false;
		}

		private void PruneObservers()
		{
			for (int i = _observers.Count - 1; i >= 0; i--)
				if (_observers[i] == null) _observers.RemoveAt(i);
		}

		private void ProcessQueues()
		{
			for (int n = 0; n < TeardownsPerFrame && _pendingRelease.Count > 0; n++)
			{
				int lotIndex = _pendingRelease[_pendingRelease.Count - 1];
				_pendingRelease.RemoveAt(_pendingRelease.Count - 1);
				ReleaseInterior(lotIndex);
			}

			for (int n = 0; n < BuildsPerFrame && _pendingBuild.Count > 0; n++)
			{
				int lotIndex = _pendingBuild[_pendingBuild.Count - 1];
				_pendingBuild.RemoveAt(_pendingBuild.Count - 1);
				RealiseInterior(lotIndex);
			}
		}

		private void RealiseInterior(int lotIndex)
		{
			if (_interiors.ContainsKey(lotIndex)) return;

			var lot = _layout.Lots[lotIndex];
			var interior = CityBuilder.BuildInterior(lot, _kit, _interiorRoot, _seed);
			if (interior == null) return;

			_interiors[lotIndex] = interior;

			// The solid shell and the walk-in version must never both be visible — one is the other's LOD.
			if (_shells.TryGetValue(lotIndex, out var shell) && shell != null)
				shell.SetActive(false);

			BuildingRealised?.Invoke(lot, interior);
		}

		private void ReleaseInterior(int lotIndex)
		{
			if (!_interiors.TryGetValue(lotIndex, out var interior)) return;

			BuildingReleased?.Invoke(_layout.Lots[lotIndex], interior);

			_interiors.Remove(lotIndex);
			if (interior != null) Destroy(interior);

			if (_shells.TryGetValue(lotIndex, out var shell) && shell != null)
				shell.SetActive(true);
		}

		/// <summary>True when a lot's walk-in interior currently exists. Used by content spawners that must not
		/// attach to a building that is still a solid box.</summary>
		public bool IsRealised(int lotIndex) => _interiors.ContainsKey(lotIndex);

		public GameObject InteriorOf(int lotIndex) =>
			_interiors.TryGetValue(lotIndex, out var go) ? go : null;
	}
}
