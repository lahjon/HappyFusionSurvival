using System;
using Fusion;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starter.Shooter
{
	/// <summary>
	/// Networked electricity grid. Splits the world into a flat XZ grid of zones and tracks a 0..1
	/// power level per zone. Power is full everywhere during the Day "Town" phase; once the Purge
	/// (<see cref="MatchPhase.Night"/>) begins the state authority cuts zones <b>edges-first</b> so the
	/// lit "safe core" shrinks toward the map center as the night runs down — funnelling players together.
	///
	/// State authority is the only writer of <see cref="ZonePower"/> and advances the blackout schedule in
	/// <see cref="FixedUpdateNetwork"/>. Every other peer reads the replicated array and derives its view
	/// identically; consumers (lights, powered devices) react to <see cref="PowerChanged"/> — the local
	/// mirror of the networked <see cref="PowerVersion"/> counter, exactly like <see cref="MatchManager.PhaseChanged"/>.
	///
	/// Phase is read from <see cref="MatchManager"/> (the single source of truth), never local <c>Time.time</c>.
	///
	/// Scene-resident singleton — add this component to the GameManager NetworkObject in 03_Shooter.unity
	/// (the same object that hosts <see cref="MatchManager"/>/<see cref="TeamManager"/>).
	/// </summary>
	public sealed class LightGrid : NetworkBehaviour
	{
		public static LightGrid Instance { get; private set; }

		/// <summary>Fires on every peer whenever any zone's power changes (and once on Spawned for late-joiners).
		/// Subscribers must be idempotent — re-read the zones they care about each time.</summary>
		public static event Action PowerChanged;

		/// <summary>Compile-time cap on zones (NetworkArray capacity). Supports up to a 16×16 grid.</summary>
		public const int MaxZones = 256;

		[Header("Grid layout (XZ plane, Y ignored)")]
		[Tooltip("World-space minimum corner (X,Z) of the grid. The grid extends +X and +Z from here.")]
		public Vector3 GridOrigin = Vector3.zero;

		[Tooltip("Size of one zone cell in world units.")]
		[Min(0.1f)] public float CellSize = 20f;

		[Tooltip("Number of zone columns along world X.")]
		[Min(1)] public int ColumnsX = 8;

		[Tooltip("Number of zone columns along world Z.")]
		[Min(1)] public int ColumnsZ = 8;

		[Header("Night blackout")]
		[Tooltip("Fraction of zones still powered at the very end of night. The lit core shrinks toward the center to this size across the Night phase.")]
		[Range(0f, 1f)] public float EndLitFraction = 0.15f;

		[Tooltip("Y height at which the editor gizmo grid is drawn (purely cosmetic).")]
		public float GizmoHeight = 0f;

		[Header("Debug overlay")]
		[Tooltip("Draw the live zone grid in the Game view (green = powered, red = dark). Can also be toggled at runtime " +
			"with the 'lightgrid_debug' console command. Local view only — never affects gameplay or networking.")]
		[SerializeField] private bool _drawDebugOverlay = false;

		[Tooltip("Opacity of the debug overlay fill quads.")]
		[Range(0f, 1f)] [SerializeField] private float _overlayAlpha = 0.35f;

		/// <summary>Runtime toggle for the in-game overlay (set by the <c>lightgrid_debug</c> console command). OR'd with
		/// the serialized <see cref="_drawDebugOverlay"/> so either source turns it on.</summary>
		public static bool DebugDraw;

		private Material _overlayMat;

		// =========================================================================
		// Networked state
		// =========================================================================

		/// <summary>Per-zone power level in [0,1]. Indexed <c>cz * ColumnsX + cx</c>. Only state authority writes.</summary>
		[Networked, Capacity(MaxZones)]
		private NetworkArray<float> ZonePower { get; }

		/// <summary>Bumped by the authority whenever <see cref="ZonePower"/> changes, so peers can react via a single
		/// OnChangedRender instead of diffing the array each frame.</summary>
		[Networked, OnChangedRender(nameof(OnPowerVersionChanged))]
		private int PowerVersion { get; set; }

		// =========================================================================
		// Local (non-networked) derived data
		// =========================================================================

		/// <summary>Zone indices sorted outermost-first (descending distance from grid center). Blackouts walk this
		/// order, so the lit area collapses toward the center. Deterministic — identical on every peer.</summary>
		private int[] _blackoutOrder = Array.Empty<int>();
		private int _orderedForZoneCount = -1;

		/// <summary>Set by the debug console (<c>blackout</c>). While true the authority holds a forced lit fraction
		/// instead of running the night schedule. Authority-only — the schedule only runs there.</summary>
		private bool  _debugHold;
		private float _debugLitFraction;

		/// <summary>Set by the <see cref="Blackout"/> match event. While true the authority forces every zone dark,
		/// overriding the phase schedule (but yielding to the debug hold). Authority-only.</summary>
		private bool _eventBlackout;

		public int ZoneCount => Mathf.Min(Mathf.Max(1, ColumnsX) * Mathf.Max(1, ColumnsZ), MaxZones);

		// =========================================================================
		// Lifecycle
		// =========================================================================

		public override void Spawned()
		{
			Instance = this;
			RebuildBlackoutOrder();

			// Authority boots the grid fully powered so Day starts lit; clients receive the replicated snapshot.
			if (HasStateAuthority)
				SetAllZones(1f);

			// Late-joiners (and anything enabled before us) get a chance to read current power immediately.
			PowerChanged?.Invoke();
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (Instance == this) Instance = null;
		}

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false) return;

			if (_debugHold)
			{
				ApplyLitFraction(_debugLitFraction);
				return;
			}

			if (_eventBlackout)
			{
				// Blackout event overrides the schedule: whole town dark.
				ApplyLitFraction(0f);
				return;
			}

			var mm = MatchManager.Instance;
			if (mm == null)
			{
				// No match controller yet — fail safe to fully powered.
				ApplyLitFraction(1f);
				return;
			}

			switch (mm.Phase)
			{
				case MatchPhase.Night:
					// Night progress 0..1. Lit fraction shrinks from 1 → EndLitFraction across the phase.
					float t = mm.NightMaxDuration > 0f
						? Mathf.Clamp01(1f - mm.RemainingPhaseSeconds / mm.NightMaxDuration)
						: 1f;
					ApplyLitFraction(Mathf.Lerp(1f, EndLitFraction, t));
					break;

				default:
					// Lobby / Day / DuskWarning / MatchOver — always on (dusk flicker is handled light-side).
					ApplyLitFraction(1f);
					break;
			}
		}

		// =========================================================================
		// Public read API (all peers)
		// =========================================================================

		/// <summary>Power [0,1] for the zone containing <paramref name="worldPos"/>. Returns 1 when the position is
		/// outside the authored grid, so objects placed beyond the grid never go dark.</summary>
		public float GetZonePowerAt(Vector3 worldPos)
		{
			int zone = WorldToZone(worldPos);
			return zone < 0 ? 1f : GetZonePower(zone);
		}

		/// <summary>Power [0,1] for an explicit zone index. Out-of-range → 1 (powered).</summary>
		public float GetZonePower(int zone)
		{
			if (zone < 0 || zone >= ZoneCount) return 1f;
			return ZonePower[zone];
		}

		/// <summary>Zone index for a world position, or -1 if the position falls outside the grid.</summary>
		public int WorldToZone(Vector3 worldPos)
		{
			if (CellSize <= 0f) return -1;
			int cx = Mathf.FloorToInt((worldPos.x - GridOrigin.x) / CellSize);
			int cz = Mathf.FloorToInt((worldPos.z - GridOrigin.z) / CellSize);
			if (cx < 0 || cx >= ColumnsX || cz < 0 || cz >= ColumnsZ) return -1;
			return cz * ColumnsX + cx;
		}

		/// <summary>World-space center of a zone cell (used for the edges-first ordering and gizmos).</summary>
		public Vector3 ZoneCenter(int zone)
		{
			int cx = zone % ColumnsX;
			int cz = zone / ColumnsX;
			return new Vector3(
				GridOrigin.x + (cx + 0.5f) * CellSize,
				GizmoHeight,
				GridOrigin.z + (cz + 0.5f) * CellSize);
		}

		// =========================================================================
		// Debug (Quantum Console)
		// =========================================================================

		/// <summary>Debug-only: force a lit fraction (0 = full blackout, 1 = all on) from any peer. Routes to the state
		/// authority and freezes the night schedule so the value holds. Use <see cref="RPC_DebugRestorePower"/> to resume.</summary>
		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_DebugSetLitFraction(float fraction)
		{
			if (HasStateAuthority == false) return;
			_debugHold = true;
			_debugLitFraction = Mathf.Clamp01(fraction);
			ApplyLitFraction(_debugLitFraction);
		}

		/// <summary>Debug-only: release the forced blackout and resume the phase-driven schedule (powers everything on now).</summary>
		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_DebugRestorePower()
		{
			if (HasStateAuthority == false) return;
			_debugHold = false;
			ApplyLitFraction(1f);
		}

		// =========================================================================
		// Event control (called by the Blackout match event, host only)
		// =========================================================================

		/// <summary>Host-only. Force the whole grid dark (<paramref name="on"/> = true) or release back to the
		/// phase-driven schedule. Driven by the <see cref="Blackout"/> match event.</summary>
		public void HostSetBlackout(bool on)
		{
			if (HasStateAuthority == false) return;
			_eventBlackout = on;
			if (on) ApplyLitFraction(0f);
			// When released, FixedUpdateNetwork resumes the normal schedule on the next tick.
		}

		// =========================================================================
		// Internal — authority writes
		// =========================================================================

		/// <summary>Power the <paramref name="litFraction"/> most-central zones (1) and black out the rest (0),
		/// walking <see cref="_blackoutOrder"/> so outer zones drop first.</summary>
		private void ApplyLitFraction(float litFraction)
		{
			RebuildBlackoutOrder();

			int count   = ZoneCount;
			int litCount = Mathf.Clamp(Mathf.CeilToInt(litFraction * count), 0, count);
			int darkCount = count - litCount;

			bool changed = false;
			// The first `darkCount` entries of the outermost-first order go dark; the central remainder stays lit.
			for (int i = 0; i < count; i++)
			{
				int zone   = _blackoutOrder[i];
				float want = i < darkCount ? 0f : 1f;
				if (ZonePower[zone] != want)
				{
					ZonePower.Set(zone, want);
					changed = true;
				}
			}

			if (changed) PowerVersion++;
		}

		private void SetAllZones(float value)
		{
			bool changed = false;
			for (int i = 0; i < ZoneCount; i++)
			{
				if (ZonePower[i] != value)
				{
					ZonePower.Set(i, value);
					changed = true;
				}
			}
			if (changed) PowerVersion++;
		}

		/// <summary>(Re)compute the outermost-first zone order if the grid dimensions changed. Pure function of the
		/// grid layout — deterministic, so authority and clients agree without networking the order.</summary>
		private void RebuildBlackoutOrder()
		{
			int count = ZoneCount;
			if (_orderedForZoneCount == count && _blackoutOrder.Length == count) return;

			_blackoutOrder = new int[count];
			for (int i = 0; i < count; i++) _blackoutOrder[i] = i;

			// Geometric center of the grid in world space.
			Vector3 center = new Vector3(
				GridOrigin.x + ColumnsX * CellSize * 0.5f,
				GizmoHeight,
				GridOrigin.z + ColumnsZ * CellSize * 0.5f);

			Array.Sort(_blackoutOrder, (a, b) =>
			{
				float da = (ZoneCenter(a) - center).sqrMagnitude;
				float db = (ZoneCenter(b) - center).sqrMagnitude;
				int cmp = db.CompareTo(da); // descending — farthest (outermost) first
				return cmp != 0 ? cmp : a.CompareTo(b); // tie-break by index for determinism
			});

			_orderedForZoneCount = count;
		}

		private void OnPowerVersionChanged()
		{
			PowerChanged?.Invoke();
		}

		// =========================================================================
		// Debug overlay (Game view, runtime) — green = powered, red = dark
		// =========================================================================

		private void OnDestroy()
		{
			if (_overlayMat != null)
			{
				if (Application.isPlaying) Destroy(_overlayMat);
				else                       DestroyImmediate(_overlayMat);
				_overlayMat = null;
			}
		}

		/// <summary>Immediate-mode overlay drawn once per rendering camera. Flat translucent quads on the XZ plane,
		/// coloured green for powered zones and red for dark ones, drawn on top of geometry so the whole grid is
		/// visible at a glance. Local view only — reads the replicated power, writes nothing.</summary>
		private void OnRenderObject()
		{
			if (!_drawDebugOverlay && !DebugDraw) return;
			if (CellSize <= 0f) return;
			if (!EnsureOverlayMaterial()) return;

			// Before this grid is networked-live (edit mode / not yet spawned) the array reads as 0; show the layout
			// fully "powered" so it previews as green rather than a misleading all-red.
			bool live = Application.isPlaying && Object != null && Object.IsValid;

			Color powered = new Color(0.15f, 1f, 0.25f, _overlayAlpha);
			Color dark    = new Color(1f, 0.12f, 0.12f, _overlayAlpha);
			float inset   = CellSize * 0.04f;
			float y       = GizmoHeight + 0.05f;

			_overlayMat.SetPass(0);
			GL.PushMatrix();

			// Filled cells.
			GL.Begin(GL.QUADS);
			for (int cz = 0; cz < ColumnsZ; cz++)
			for (int cx = 0; cx < ColumnsX; cx++)
			{
				int zone = cz * ColumnsX + cx;
				if (zone >= MaxZones) continue;

				float p = live ? GetZonePower(zone) : 1f;
				GL.Color(Color.Lerp(dark, powered, p));

				float x0 = GridOrigin.x + cx * CellSize + inset;
				float x1 = GridOrigin.x + (cx + 1) * CellSize - inset;
				float z0 = GridOrigin.z + cz * CellSize + inset;
				float z1 = GridOrigin.z + (cz + 1) * CellSize - inset;

				GL.Vertex3(x0, y, z0);
				GL.Vertex3(x1, y, z0);
				GL.Vertex3(x1, y, z1);
				GL.Vertex3(x0, y, z1);
			}
			GL.End();

			// Bright cell outlines for readability.
			GL.Begin(GL.LINES);
			GL.Color(new Color(1f, 0.95f, 0.4f, Mathf.Min(1f, _overlayAlpha + 0.4f)));
			for (int cz = 0; cz < ColumnsZ; cz++)
			for (int cx = 0; cx < ColumnsX; cx++)
			{
				if (cz * ColumnsX + cx >= MaxZones) continue;

				float x0 = GridOrigin.x + cx * CellSize;
				float x1 = GridOrigin.x + (cx + 1) * CellSize;
				float z0 = GridOrigin.z + cz * CellSize;
				float z1 = GridOrigin.z + (cz + 1) * CellSize;

				GL.Vertex3(x0, y, z0); GL.Vertex3(x1, y, z0);
				GL.Vertex3(x1, y, z0); GL.Vertex3(x1, y, z1);
				GL.Vertex3(x1, y, z1); GL.Vertex3(x0, y, z1);
				GL.Vertex3(x0, y, z1); GL.Vertex3(x0, y, z0);
			}
			GL.End();

			GL.PopMatrix();
		}

		/// <summary>Lazily build the unlit, alpha-blended, depth-test-off material used for the GL overlay (so it draws
		/// over world geometry and is always visible). Returns false if the colored shader is unavailable.</summary>
		private bool EnsureOverlayMaterial()
		{
			if (_overlayMat != null) return true;

			var shader = Shader.Find("Hidden/Internal-Colored");
			if (shader == null) return false;

			_overlayMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
			_overlayMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
			_overlayMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
			_overlayMat.SetInt("_Cull",     (int)CullMode.Off);
			_overlayMat.SetInt("_ZWrite",   0);
			_overlayMat.SetInt("_ZTest",    (int)CompareFunction.Always); // draw over geometry — see every zone
			return true;
		}

		// =========================================================================
		// Editor
		// =========================================================================

#if UNITY_EDITOR
		private void OnValidate()
		{
			ColumnsX = Mathf.Max(1, ColumnsX);
			ColumnsZ = Mathf.Max(1, ColumnsZ);
			if (ColumnsX * ColumnsZ > MaxZones)
				Debug.LogWarning($"[LightGrid] {ColumnsX}×{ColumnsZ} = {ColumnsX * ColumnsZ} zones exceeds MaxZones ({MaxZones}); extra zones are ignored.", this);
		}

		private void OnDrawGizmosSelected()
		{
			if (CellSize <= 0f) return;

			bool playing = Application.isPlaying && Instance == this && Object != null && Object.IsValid;
			for (int cz = 0; cz < ColumnsZ; cz++)
			for (int cx = 0; cx < ColumnsX; cx++)
			{
				int zone = cz * ColumnsX + cx;
				if (zone >= MaxZones) continue;

				Vector3 cellCenter = new Vector3(
					GridOrigin.x + (cx + 0.5f) * CellSize,
					GizmoHeight,
					GridOrigin.z + (cz + 0.5f) * CellSize);

				if (playing)
				{
					float p = GetZonePower(zone);
					Gizmos.color = Color.Lerp(new Color(1f, 0.1f, 0.1f, 0.25f), new Color(0.2f, 1f, 0.3f, 0.25f), p);
					Gizmos.DrawCube(cellCenter, new Vector3(CellSize * 0.96f, 0.02f, CellSize * 0.96f));
				}

				Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.6f);
				Gizmos.DrawWireCube(cellCenter, new Vector3(CellSize, 0.02f, CellSize));
			}
		}
#endif
	}
}
