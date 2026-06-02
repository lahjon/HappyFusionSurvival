using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// A named staging area in the town. Each team is assigned one distinct <see cref="Zone"/> at match start
	/// (see <see cref="ZoneManager"/>); a player can only arm their PvP weapons during Night once they stand
	/// inside their team's zone. All <see cref="SpawnPoint"/>s should sit inside a zone (tag them via
	/// <see cref="SpawnPoint.ZoneId"/>).
	///
	/// Pure scene geometry — identical on every peer, so this is a plain <see cref="MonoBehaviour"/> with no
	/// networked state. The containment test is horizontal (XZ) with a generous vertical band so standing on
	/// uneven terrain inside the footprint still counts.
	/// </summary>
	public sealed class Zone : MonoBehaviour
	{
		[Tooltip("Stable id for this zone. Teams are mapped to a ZoneId at match start; SpawnPoints reference it.")]
		public int ZoneId;

		[Header("Footprint")]
		[Tooltip("If assigned (or found on this object), the box's local XZ extents define the footprint. " +
		         "Otherwise the Radius below is used as a horizontal circle around the transform.")]
		public BoxCollider Box;

		[Tooltip("Horizontal radius used when no Box is assigned.")]
		[Min(0.5f)] public float Radius = 8f;

		[Tooltip("Half-height of the vertical band (metres above/below the footprint) a player may stand in and still count as inside.")]
		[Min(0.5f)] public float VerticalLeniency = 6f;

		private void Reset()
		{
			Box = GetComponent<BoxCollider>();
		}

		private void Awake()
		{
			if (Box == null)
				Box = GetComponent<BoxCollider>();
		}

		/// <summary>True when <paramref name="worldPos"/> is within the zone footprint (XZ) and vertical band.</summary>
		public bool Contains(Vector3 worldPos)
		{
			if (Box != null)
			{
				// Test in the box's local space so a rotated zone still works. Ignore Y within the configured band.
				Vector3 local = Box.transform.InverseTransformPoint(worldPos) - Box.center;
				Vector3 ext = Box.size * 0.5f;
				if (Mathf.Abs(local.x) > ext.x) return false;
				if (Mathf.Abs(local.z) > ext.z) return false;
				return Mathf.Abs(local.y) <= ext.y + VerticalLeniency;
			}

			Vector3 d = worldPos - transform.position;
			if (Mathf.Abs(d.y) > VerticalLeniency) return false;
			d.y = 0f;
			return d.sqrMagnitude <= Radius * Radius;
		}

		private void OnDrawGizmos()
		{
			Color c = ColorForId(ZoneId);
			c.a = 0.18f;
			Gizmos.color = c;

			var box = Box != null ? Box : GetComponent<BoxCollider>();
			if (box != null)
			{
				Matrix4x4 prev = Gizmos.matrix;
				Gizmos.matrix = box.transform.localToWorldMatrix;
				Gizmos.DrawCube(box.center, box.size);
				c.a = 0.9f;
				Gizmos.color = c;
				Gizmos.DrawWireCube(box.center, box.size);
				Gizmos.matrix = prev;
			}
			else
			{
				Gizmos.DrawSphere(transform.position, Radius);
				c.a = 0.9f;
				Gizmos.color = c;
				Gizmos.DrawWireSphere(transform.position, Radius);
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
