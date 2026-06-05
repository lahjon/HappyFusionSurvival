using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-view-only manager that draws a floating chevron above every other player: a green marker over
	/// teammates (visible through walls, within <see cref="AllyRange"/>) and a red marker over hostiles
	/// (Night only, line-of-sight only — hidden by walls). It also draws a single house beacon over the local
	/// player's own team zone (through walls; within <see cref="HouseRange"/> by Day, map-wide from DuskWarning on;
	/// hidden once you're inside the zone). Pure presentation — no networked state, no RPCs. Reads existing
	/// networked state: <see cref="Player.Owner"/>, <see cref="TeamManager.SameTeam"/>, <see cref="Health.IsAlive"/>,
	/// <see cref="MatchManager.Phase"/>, <see cref="ZoneManager.ZoneOfPlayer"/>.
	///
	/// Self-bootstrapping (see <see cref="Bootstrap"/>) — no scene or prefab wiring. Markers are pooled
	/// world-space meshes parented under this manager and repositioned every frame, so player despawns need
	/// no per-marker cleanup beyond dropping them from the pool.
	/// </summary>
	public sealed class WorldMarkerManager : MonoBehaviour
	{
		[Header("Ally markers (green, through walls)")]
		[Tooltip("Allies show a marker only within this many metres of the local player.")]
		public float AllyRange = 15f;
		public Color AllyColor = new Color(0.25f, 1f, 0.35f, 1f);

		[Header("Enemy markers (red, Night + line-of-sight only)")]
		public Color EnemyColor = new Color(1f, 0.2f, 0.18f, 1f);
		[Tooltip("Geometry on these layers blocks the line-of-sight check that reveals enemy markers. Default = everything.")]
		public LayerMask OcclusionMask = ~0;

		[Header("House marker (your team's zone — through walls)")]
		[Tooltip("During Day the house beacon shows only within this many metres; from DuskWarning onward it shows map-wide.")]
		public float HouseRange = 20f;
		[Tooltip("Colour of the through-walls beacon over your team's house / zone.")]
		public Color HouseColor = new Color(1f, 0.82f, 0.2f, 1f);
		[Tooltip("Metres above the zone centre the house beacon floats.")]
		public float HouseMarkerHeight = 4f;
		[Tooltip("Base world size of the house beacon (read at map distance, so larger than player markers).")]
		public float HouseMarkerScale = 0.55f;

		[Header("Placement / feel")]
		[Tooltip("Metres above the player root the marker floats.")]
		public float MarkerHeight = 2.3f;
		[Tooltip("Base world size of the chevron.")]
		public float MarkerScale = 0.6f;
		[Tooltip("How fast the marker pops in / out (units of the 0..1 show factor per second).")]
		public float PopSpeed = 6f;

		private static WorldMarkerManager _instance;

		private Mesh _chevronMesh;
		private Mesh _houseMesh;
		private Material _allyMat;
		private Material _enemyMat;
		private Transform _camera;
		private Marker _houseMarker;

		private readonly Dictionary<Player, Marker> _markers = new(32);
		private readonly List<Player> _stale = new(8);
		private static readonly int ColorId = Shader.PropertyToID("_Color");

		private sealed class Marker
		{
			public GameObject Go;
			public Transform Tr;
			public MeshRenderer Renderer;
			public MaterialPropertyBlock Mpb;
			public float Shown; // 0..1 lerped show factor for the pop effect
			public bool WasAlly;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Bootstrap()
		{
			if (_instance != null) return;
			var go = new GameObject("WorldMarkerManager");
			DontDestroyOnLoad(go);
			_instance = go.AddComponent<WorldMarkerManager>();
		}

		private void Awake()
		{
			if (_instance != null && _instance != this) { Destroy(gameObject); return; }
			_instance = this;

			_chevronMesh = BuildChevronMesh();
			_houseMesh = BuildHouseMesh();

			var shader = Resources.Load<Shader>("Indicator");
			if (shader == null)
			{
				Debug.LogError("[WorldMarkerManager] Missing shader Resources/Indicator. Markers disabled.");
				enabled = false;
				return;
			}

			// Ally: draws over geometry (through walls). Enemy: normal depth test (+ explicit LOS gate).
			_allyMat = new Material(shader) { name = "AllyMarker" };
			_allyMat.SetFloat("_ZTest", (float)CompareFunction.Always);
			_allyMat.renderQueue = 4000;

			_enemyMat = new Material(shader) { name = "EnemyMarker" };
			_enemyMat.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
			_enemyMat.renderQueue = 3000;
		}

		private void OnDestroy()
		{
			if (_chevronMesh != null) Destroy(_chevronMesh);
			if (_houseMesh != null) Destroy(_houseMesh);
			if (_houseMarker != null && _houseMarker.Go != null) Destroy(_houseMarker.Go);
			if (_allyMat != null) Destroy(_allyMat);
			if (_enemyMat != null) Destroy(_enemyMat);
		}

		private void LateUpdate()
		{
			if (_camera == null)
			{
				var cam = Camera.main;
				if (cam != null) _camera = cam.transform;
			}

			var local = ResolveLocalPlayer();
			float dt = Time.deltaTime;

			// Drive a marker for every known player; collect despawned ones for pool removal.
			var roster = Player.All;
			for (int i = 0; i < roster.Count; i++)
			{
				var p = roster[i];
				if (p == null || p.Object == null) continue;
				UpdateMarkerFor(p, local, dt);
			}

			UpdateHouseMarker(local, dt);

			PruneStaleMarkers();
		}

		private void UpdateMarkerFor(Player p, Player local, float dt)
		{
			bool wantAlly = false;
			bool visible = false;

			// Never mark yourself, and need a valid local reference + camera to place anything.
			if (local != null && p != local && _camera != null && p.Health != null)
			{
				var team = TeamManager.Instance;
				bool ally = team != null && team.SameTeam(local.Owner, p.Owner);
				wantAlly = ally;

				if (ally)
				{
					bool aliveOrDowned = p.Health.IsAlive || p.IsDowned;
					float sqr = (p.transform.position - local.transform.position).sqrMagnitude;
					visible = aliveOrDowned && sqr <= AllyRange * AllyRange;
				}
				else
				{
					bool nightPvp = MatchManager.Instance != null &&
					                (MatchManager.Instance.Phase == MatchPhase.Night || MatchManager.Instance.IsPvpForced);
					visible = p.Health.IsAlive && nightPvp && HasLineOfSight(p);
				}
			}

			// Skip allocating a marker for a player that's hidden and has no fade-out left to play.
			bool hasMarker = _markers.TryGetValue(p, out var m);
			if (visible == false && (hasMarker == false || m.Shown <= 0.001f))
			{
				if (hasMarker && m.Go.activeSelf) m.Go.SetActive(false);
				return;
			}

			if (hasMarker == false)
			{
				m = CreateMarker(_chevronMesh, _enemyMat);
				_markers.Add(p, m);
			}

			// Swap material when an ally/enemy relationship flips (rare; e.g. team reassignment between rounds).
			if (m.WasAlly != wantAlly || m.Renderer.sharedMaterial == null)
			{
				m.Renderer.sharedMaterial = wantAlly ? _allyMat : _enemyMat;
				m.WasAlly = wantAlly;
			}

			Vector3 headPos = p.transform.position + Vector3.up * MarkerHeight;
			Color baseColor = wantAlly ? AllyColor : EnemyColor;
			ApplyPopRender(m, visible, headPos, baseColor, MarkerScale, dt);
		}

		/// <summary>Beacon floating over the local player's own team zone ("home"). Drawn through walls (shares the
		/// ally material's ZTest-Always pass). <b>Day:</b> shows only within <see cref="HouseRange"/>. <b>From
		/// DuskWarning onward</b> (the rush home to arm for the Purge): shows map-wide. Hidden whenever you're already
		/// standing inside your zone, and before zones are assigned (Lobby) / after the match (MatchOver) — all of
		/// which fall through to <c>visible = false</c> and fade out via the same pop logic as player markers.</summary>
		private void UpdateHouseMarker(Player local, float dt)
		{
			if (_camera == null) return;

			var zm = ZoneManager.Instance;
			Zone zone = (local != null && zm != null) ? zm.ZoneOfPlayer(local.Owner) : null;

			bool visible = false;
			// Default to the marker's current spot so a fade-out plays in place even when we momentarily lack a zone.
			Vector3 worldPos = _houseMarker != null ? _houseMarker.Tr.position : default;

			if (zone != null)
			{
				// An override anchor on the zone positions the beacon exactly; otherwise float above the footprint centre.
				Vector3 anchor = zone.IndicatorPosition;
				worldPos = zone.HasIndicatorAnchor ? anchor : anchor + Vector3.up * HouseMarkerHeight;

				// Already home → nothing to point at; let it fade out at the house.
				if (zone.Contains(local.transform.position) == false)
				{
					var phase = MatchManager.Instance != null ? MatchManager.Instance.Phase : MatchPhase.Lobby;
					if (phase == MatchPhase.Day)
						visible = (anchor - local.transform.position).sqrMagnitude <= HouseRange * HouseRange;
					else if (phase == MatchPhase.DuskWarning || phase == MatchPhase.Night)
						visible = true; // rush-home beacon — whole map
				}
			}

			if (_houseMarker == null)
			{
				if (visible == false) return; // don't allocate until first shown
				_houseMarker = CreateMarker(_houseMesh, _allyMat);
			}

			ApplyPopRender(_houseMarker, visible, worldPos, HouseColor, HouseMarkerScale, dt);
		}

		/// <summary>Shared pop-in / fade-out feel for every marker (player chevrons and the house beacon): a smooth
		/// MoveTowards on a 0..1 show factor that scales (EaseOutBack overshoot) and fades alpha together — never a
		/// hard on/off. Deactivates the GameObject once fully collapsed. Per-marker colour + alpha go through a
		/// property block so pooled markers stay independent without instancing the shared material.</summary>
		private void ApplyPopRender(Marker m, bool visible, Vector3 worldPos, Color baseColor, float scale, float dt)
		{
			m.Shown = Mathf.MoveTowards(m.Shown, visible ? 1f : 0f, PopSpeed * dt);
			if (m.Shown <= 0.001f)
			{
				if (m.Go.activeSelf) m.Go.SetActive(false);
				return;
			}

			if (m.Go.activeSelf == false) m.Go.SetActive(true);

			m.Tr.position = worldPos;
			m.Tr.rotation = _camera.rotation; // billboard toward the camera
			m.Tr.localScale = Vector3.one * (scale * EaseOutBack(m.Shown));

			Color c = baseColor;
			c.a *= Mathf.Clamp01(m.Shown);
			m.Mpb.SetColor(ColorId, c);
			m.Renderer.SetPropertyBlock(m.Mpb);
		}

		/// <summary>True if nothing on <see cref="OcclusionMask"/> sits between the camera and the target's chest.
		/// The ray stops just short of the target so the target's own collider never counts as occlusion.</summary>
		private bool HasLineOfSight(Player p)
		{
			Vector3 eye = _camera.position;
			Vector3 chest = p.transform.position + Vector3.up * 1.2f;
			Vector3 delta = chest - eye;
			float dist = delta.magnitude;
			if (dist <= 0.5f) return true;
			return Physics.Raycast(eye, delta / dist, dist - 0.4f, OcclusionMask, QueryTriggerInteraction.Ignore) == false;
		}

		private Player ResolveLocalPlayer()
		{
			var roster = Player.All;
			for (int i = 0; i < roster.Count; i++)
			{
				var p = roster[i];
				if (p != null && p.Object != null && p.Object.HasInputAuthority)
					return p;
			}
			return null;
		}

		private void PruneStaleMarkers()
		{
			_stale.Clear();
			foreach (var kvp in _markers)
			{
				if (kvp.Key == null || kvp.Key.Object == null)
					_stale.Add(kvp.Key);
			}
			for (int i = 0; i < _stale.Count; i++)
			{
				if (_markers.TryGetValue(_stale[i], out var m) && m.Go != null)
					Destroy(m.Go);
				_markers.Remove(_stale[i]);
			}
		}

		private Marker CreateMarker(Mesh mesh, Material material)
		{
			var go = new GameObject("Marker");
			go.transform.SetParent(transform, false);
			var mf = go.AddComponent<MeshFilter>();
			mf.sharedMesh = mesh;
			var mr = go.AddComponent<MeshRenderer>();
			mr.shadowCastingMode = ShadowCastingMode.Off;
			mr.receiveShadows = false;
			mr.lightProbeUsage = LightProbeUsage.Off;
			mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
			mr.sharedMaterial = material;
			return new Marker
			{
				Go = go,
				Tr = go.transform,
				Renderer = mr,
				Mpb = new MaterialPropertyBlock(),
				Shown = 0f,
				WasAlly = false,
			};
		}

		/// <summary>A solid downward-pointing chevron/arrow in the local XY plane (tip at -Y). Billboarded so
		/// +Z faces the camera; Cull Off in the shader makes it double-sided.</summary>
		private static Mesh BuildChevronMesh()
		{
			// Outer V (pointing edge) + inner V (offset up) form a thick chevron — two quads.
			const float w = 0.5f;   // half width
			const float top = 0.5f; // y of the arm tops
			const float th = 0.32f; // arm thickness (vertical drop of the inner V)

			var verts = new Vector3[]
			{
				new Vector3(-w,      top,        0f), // 0 outer left top
				new Vector3( 0f,     0f,         0f), // 1 outer tip (bottom)
				new Vector3( w,      top,        0f), // 2 outer right top
				new Vector3(-w + 0f, top + th,   0f), // 3 inner left top
				new Vector3( 0f,     th,         0f), // 4 inner tip
				new Vector3( w - 0f, top + th,   0f), // 5 inner right top
			};

			// Left arm quad (0,1,4,3) and right arm quad (1,2,5,4), two tris each.
			var tris = new int[]
			{
				0, 1, 4,  0, 4, 3,   // left arm
				1, 2, 5,  1, 5, 4,   // right arm
			};

			var mesh = new Mesh { name = "ChevronMarker" };
			mesh.SetVertices(new List<Vector3>(verts));
			mesh.SetTriangles(tris, 0);
			mesh.RecalculateBounds();
			return mesh;
		}

		/// <summary>A solid house silhouette (square body + triangular roof) in the local XY plane, sized to roughly
		/// match the chevron's footprint. Billboarded like the chevron; Cull Off makes it double-sided.</summary>
		private static Mesh BuildHouseMesh()
		{
			var verts = new List<Vector3>
			{
				new Vector3(-0.35f, -0.5f,  0f), // 0 body bottom-left
				new Vector3( 0.35f, -0.5f,  0f), // 1 body bottom-right
				new Vector3( 0.35f,  0.12f, 0f), // 2 body top-right (eave)
				new Vector3(-0.35f,  0.12f, 0f), // 3 body top-left  (eave)
				new Vector3(-0.5f,   0.12f, 0f), // 4 roof left eave
				new Vector3( 0.5f,   0.12f, 0f), // 5 roof right eave
				new Vector3( 0f,     0.6f,  0f), // 6 roof apex
			};

			var tris = new int[]
			{
				0, 3, 2,  0, 2, 1,   // body quad
				4, 6, 5,             // roof triangle
			};

			var mesh = new Mesh { name = "HouseMarker" };
			mesh.SetVertices(verts);
			mesh.SetTriangles(tris, 0);
			mesh.RecalculateBounds();
			return mesh;
		}

		/// <summary>Ease-out-back: overshoots ~10% before settling, giving the marker a lively pop-in.</summary>
		private static float EaseOutBack(float t)
		{
			const float c1 = 1.70158f;
			const float c3 = c1 + 1f;
			float u = t - 1f;
			return 1f + c3 * (u * u * u) + c1 * (u * u);
		}
	}
}
