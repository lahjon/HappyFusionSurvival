using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// A named staging area in the town. Each team is assigned one distinct <see cref="Zone"/> at match start
	/// (see <see cref="ZoneManager"/>); a player can only arm their PvP weapons during Night once they stand
	/// inside their team's zone. Players spawn at one of the <see cref="SpawnPoints"/> that fall inside this zone —
	/// the spawn points are tied to a zone by containment, gathered by <see cref="ZoneManager"/> on init (no manual
	/// tagging).
	///
	/// The footprint (Box / Circle / Polygon), containment test and vertical band all come from
	/// <see cref="WorldArea"/>; this class only adds the zone identity used by <see cref="ZoneManager"/>.
	/// </summary>
	public sealed class Zone : WorldArea
	{
		[Header("Zone")]
		[Tooltip("Stable, unique id for this zone (teams are mapped to a ZoneId at match start). Auto-managed in the " +
			"editor: a new or duplicated zone is given the smallest free id, and collisions/negatives self-heal. You " +
			"can still set it by hand to control team→zone order; duplicates just get bumped.")]
		public int ZoneId;

		[Header("House Indicator")]
		[Tooltip("Optional override for where the house beacon (WorldMarkerManager) floats. When set, the beacon " +
			"anchors to this transform's position exactly — e.g. an empty placed over the roof or door — instead of " +
			"the footprint centre, and the manager's height offset is NOT added (you position it precisely). Leave " +
			"empty to fall back to the zone centre + the manager's HouseMarkerHeight.")]
		[SerializeField] private Transform _indicatorAnchor;

		[Header("Spawn Points")]
		[Tooltip("Spawn points sitting inside this zone. Auto-gathered by ZoneManager on init at runtime; use the " +
			"Gather button to populate/preview the list in the editor. A team assigned this zone spawns at one of these.")]
		[SerializeField] private List<SpawnPoint> _spawnPoints = new();

		/// <summary>True when an <see cref="_indicatorAnchor"/> override is assigned, so the house beacon should use
		/// <see cref="IndicatorPosition"/> verbatim instead of the footprint centre + the manager's height offset.</summary>
		public bool HasIndicatorAnchor => _indicatorAnchor != null;

		/// <summary>World position the house beacon should anchor to: the <see cref="_indicatorAnchor"/> override if
		/// set, else the footprint <see cref="WorldArea.Center"/>. Also used as the reference point for the Day-time
		/// proximity range.</summary>
		public Vector3 IndicatorPosition => _indicatorAnchor != null ? _indicatorAnchor.position : Center;

		/// <summary>The <see cref="SpawnPoint"/>s sitting inside this zone (by footprint containment). Rebuilt at
		/// runtime by <see cref="ZoneManager"/> on init; a team assigned this zone spawns at one of these (see
		/// <see cref="GameManager"/>).</summary>
		public IReadOnlyList<SpawnPoint> SpawnPoints => _spawnPoints;

		/// <summary>Clears the cached spawn points. Called by <see cref="ZoneManager"/> before a fresh distribution.</summary>
		public void ClearSpawnPoints() => _spawnPoints.Clear();

		/// <summary>Adds <paramref name="spawnPoint"/> to this zone if it sits inside the footprint. Returns true when
		/// claimed, so the distributing pass can stop at the first containing zone.</summary>
		public bool TryClaimSpawnPoint(SpawnPoint spawnPoint)
		{
			if (spawnPoint == null) return false;
			if (Contains(spawnPoint.transform.position) == false) return false;
			_spawnPoints.Add(spawnPoint);
			return true;
		}

#if UNITY_EDITOR
		// Edit-time preview of the same containment gather ZoneManager runs on init, so the list is visible/editable
		// in the Inspector without entering Play mode.
		[Button("Gather Spawn Points In Zone"), PropertyOrder(-1)]
		private void EditorGatherSpawnPoints()
		{
			_spawnPoints.Clear();
			var all = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			for (int i = 0; i < all.Length; i++)
				if (all[i] != null && Contains(all[i].transform.position))
					_spawnPoints.Add(all[i]);
			UnityEditor.EditorUtility.SetDirty(this);
		}

		// ── Auto-managed ZoneId ───────────────────────────────────────────────────
		// Reset runs when the component is first added; OnValidate runs on edit and (crucially) right after a
		// Ctrl+D duplicate, which copies the source's ZoneId. Both schedule a single coalesced scene-wide pass
		// that gives every zone a unique id, so creating zones can never silently collide (ZoneManager would
		// otherwise drop the duplicate). Power users can still hand-set ids to order team→zone; only genuine
		// collisions and negative ids are rewritten.

		private static bool _idFixupScheduled;

		private void Reset()      => ScheduleIdFixup();
		private void OnValidate() => ScheduleIdFixup();

		private static void ScheduleIdFixup()
		{
			if (_idFixupScheduled) return;
			_idFixupScheduled = true;
			UnityEditor.EditorApplication.delayCall += FixupAllZoneIds;
		}

		private static void FixupAllZoneIds()
		{
			_idFixupScheduled = false;
			if (Application.isPlaying) return;

			var zones = FindObjectsByType<Zone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			// Stable order: by current id, then instance id — so the first holder of an id keeps it and later
			// duplicates are the ones bumped, giving a deterministic, churn-free result.
			System.Array.Sort(zones, (a, b) =>
			{
				int c = a.ZoneId.CompareTo(b.ZoneId);
				return c != 0 ? c : a.GetInstanceID().CompareTo(b.GetInstanceID());
			});

			var used = new HashSet<int>();
			for (int i = 0; i < zones.Length; i++)
			{
				var z = zones[i];
				if (z == null) continue;

				int id = z.ZoneId;
				if (id < 0 || used.Contains(id))
				{
					id = 0;
					while (used.Contains(id)) id++;
				}
				if (z.ZoneId != id)
				{
					z.ZoneId = id;
					UnityEditor.EditorUtility.SetDirty(z);
				}
				used.Add(id);
			}
		}

		[Button("Renumber All Zone Ids (0..n)"), PropertyOrder(-1)]
		private void EditorRenumberAllZoneIds()
		{
			// Compact, predictable renumber by name — use when ids have drifted and you want a clean 0..n set.
			var zones = FindObjectsByType<Zone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			System.Array.Sort(zones, (a, b) => string.CompareOrdinal(a.name, b.name));
			for (int i = 0; i < zones.Length; i++)
			{
				if (zones[i] == null || zones[i].ZoneId == i) continue;
				zones[i].ZoneId = i;
				UnityEditor.EditorUtility.SetDirty(zones[i]);
			}
		}
#endif

		private void OnDrawGizmos()
		{
			Color c = ColorForId(ZoneId);
			Color fill = c; fill.a = 0.18f;
			Color line = c; line.a = 0.9f;
			DrawFootprintGizmo(fill, line);

			// Show where the house beacon will anchor when an override transform is assigned.
			if (_indicatorAnchor != null)
			{
				Gizmos.color = line;
				Gizmos.DrawLine(Center, _indicatorAnchor.position);
				Gizmos.DrawWireSphere(_indicatorAnchor.position, 0.4f);
			}
		}

		/// <summary>Stable, readable gizmo colour per zone id so designers can tell zones apart at a glance.</summary>
		public static Color ColorForId(int id)
		{
			// Golden-ratio hue spacing keeps adjacent ids visually distinct.
			float hue = Mathf.Repeat(Mathf.Max(0, id) * 0.61803398875f, 1f);
			return Color.HSVToRGB(hue, 0.65f, 1f);
		}
	}
}
