using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Represents a spawning point in the environment. A spawn point is tied to the <see cref="Zone"/> whose
	/// footprint contains it — <see cref="ZoneManager"/> gathers the spawn points into their zone on init, and a
	/// team assigned that zone spawns at one of them. Every spawn point should fall within exactly one zone. The
	/// point's yaw (<see cref="Transform.forward"/>) is the direction a player will be looking when they spawn here
	/// — visualized by the debug arrow gizmo.
	/// </summary>
	public class SpawnPoint : MonoBehaviour
	{
		public float Radius = 1f;

		private void OnDrawGizmos()
		{
			DrawFacingArrow(GizmoColor());
		}

		private void OnDrawGizmosSelected()
		{
			Gizmos.color = GizmoColor();
			Gizmos.DrawWireSphere(transform.position, Radius);
		}

		// Colour by the zone that contains this point so designers can read zone membership at a glance. Editor-only
		// gizmo path, so the per-draw FindObjectsByType scan is acceptable.
		private Color GizmoColor()
		{
			var zones = FindObjectsByType<Zone>(FindObjectsInactive.Exclude);
			for (int i = 0; i < zones.Length; i++)
				if (zones[i] != null && zones[i].Contains(transform.position))
					return Zone.ColorForId(zones[i].ZoneId);
			return Color.white;
		}

		// Arrow centered on the spawn point along its forward — the direction the player will face on spawn.
		private void DrawFacingArrow(Color color)
		{
			Gizmos.color = color;

			Vector3 dir = transform.forward;
			dir.y = 0f;
			if (dir.sqrMagnitude < 0.0001f) return;
			dir.Normalize();

			Vector3 origin = transform.position;
			float length = Mathf.Max(1.5f, Radius * 2f);
			Vector3 tip = origin + dir * length;

			Gizmos.DrawLine(origin, tip);

			// Two-pronged arrowhead in the horizontal plane.
			float headLen = length * 0.25f;
			Vector3 right = Quaternion.Euler(0f, 155f, 0f) * dir;
			Vector3 left = Quaternion.Euler(0f, -155f, 0f) * dir;
			Gizmos.DrawLine(tip, tip + right * headLen);
			Gizmos.DrawLine(tip, tip + left * headLen);
		}
	}
}
