using System.Collections.Generic;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Shared base for scene-placed, local-only footprint volumes — a horizontal (XZ) region with a
	/// vertical leniency band, used for "is this point inside?" tests. Concrete areas layer their own
	/// domain data on top: <see cref="Zone"/> (a team's PvP-arming staging area) and
	/// <see cref="NpcMovementArea"/> (the bounds an NPC roams / defends within).
	///
	/// Pure scene geometry — identical on every peer — so this is a plain <see cref="MonoBehaviour"/>
	/// with no networked state. The footprint is defined in this transform's local space and may be a
	/// <see cref="AreaShape.Box"/>, a <see cref="AreaShape.Circle"/>, or an arbitrary
	/// <see cref="AreaShape.Polygon"/> (for irregular outlines like odd-footprint houses). Y is ignored
	/// for containment beyond <see cref="VerticalLeniency"/>, so an actor on uneven ground inside the
	/// footprint still registers as "inside".
	/// </summary>
	public abstract class WorldArea : MonoBehaviour
	{
		public enum AreaShape { Box, Circle, Polygon }

		[Header("Footprint")]
		[Tooltip("Shape of the horizontal footprint. Polygon allows an arbitrary outline for irregular buildings.")]
		public AreaShape Shape = AreaShape.Box;

		[Tooltip("Footprint center offset from this transform, in local space (X/Z used). Lets the volume sit off to the side of the object it's attached to.")]
		public Vector3 Offset = Vector3.zero;

		[Tooltip("Box dimensions in local space (X/Z = footprint; Y only sizes the gizmo and vertical band). Used when Shape = Box.")]
		public Vector3 Size = new Vector3(20f, 4f, 20f);

		[Tooltip("Horizontal radius around the center. Used when Shape = Circle.")]
		[Min(0.5f)] public float Radius = 8f;

		[Tooltip("Outline points in local XZ (x = local X, y = local Z), relative to Offset, wound in order. Used when Shape = Polygon. Needs at least 3 points.")]
		public List<Vector2> Polygon = new List<Vector2>();

		[Tooltip("Half-height of the vertical band (metres above/below the footprint plane) an actor may stand in and still count as inside.")]
		[Min(0.5f)] public float VerticalLeniency = 6f;

		/// <summary>World-space center of the footprint.</summary>
		public Vector3 Center => transform.TransformPoint(Offset);

		// =========================================================================
		// Containment / clamping / sampling (world space in, world space out)
		// =========================================================================

		/// <summary>True when <paramref name="worldPos"/> is within the footprint (XZ) and the vertical band.</summary>
		public bool Contains(Vector3 worldPos)
		{
			Vector3 local = transform.InverseTransformPoint(worldPos) - Offset;
			if (Mathf.Abs(local.y) > VerticalLeniency + VerticalHalf()) return false;
			return ContainsLocalXZ(local.x, local.z);
		}

		/// <summary>Clamps <paramref name="worldPos"/> into the footprint (XZ only; Y is preserved) and returns it in world space.</summary>
		public Vector3 ClampInside(Vector3 worldPos)
		{
			Vector3 local = transform.InverseTransformPoint(worldPos) - Offset;
			Vector2 clamped = ClampLocalXZ(new Vector2(local.x, local.z));
			Vector3 result = new Vector3(clamped.x, local.y, clamped.y) + Offset;
			return transform.TransformPoint(result);
		}

		/// <summary>
		/// Picks a random point inside the footprint (using the caller's per-instance RNG so callers
		/// sample independently) and returns it in world space, at the footprint plane's height.
		/// Returns false only for a degenerate polygon (rejection sampling found no interior point).
		/// </summary>
		public bool TryRandomLocalPoint(System.Random rng, out Vector3 world)
		{
			Vector2 local;
			switch (Shape)
			{
				case AreaShape.Circle:
				{
					// Uniform over the disk: sqrt keeps samples from bunching at the center.
					float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
					float rad = Radius * Mathf.Sqrt((float)rng.NextDouble());
					local = new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);
					break;
				}
				case AreaShape.Polygon:
				{
					if (TryRandomInPolygon(rng, out local) == false) { world = default; return false; }
					break;
				}
				default: // Box
				{
					float hx = Size.x * 0.5f, hz = Size.z * 0.5f;
					local = new Vector2(((float)rng.NextDouble() * 2f - 1f) * hx,
					                    ((float)rng.NextDouble() * 2f - 1f) * hz);
					break;
				}
			}

			world = transform.TransformPoint(new Vector3(local.x, 0f, local.y) + Offset);
			return true;
		}

		// =========================================================================
		// Local-space shape math
		// =========================================================================

		private float VerticalHalf() => Shape == AreaShape.Box ? Size.y * 0.5f : 0f;

		private bool ContainsLocalXZ(float x, float z)
		{
			switch (Shape)
			{
				case AreaShape.Circle:  return x * x + z * z <= Radius * Radius;
				case AreaShape.Polygon: return PolygonContains(x, z);
				default:                return Mathf.Abs(x) <= Size.x * 0.5f && Mathf.Abs(z) <= Size.z * 0.5f;
			}
		}

		private Vector2 ClampLocalXZ(Vector2 p)
		{
			switch (Shape)
			{
				case AreaShape.Circle:
					return p.sqrMagnitude <= Radius * Radius ? p : p.normalized * Radius;
				case AreaShape.Polygon:
					return PolygonContains(p.x, p.y) ? p : ClosestPointOnPolygon(p);
				default:
				{
					float hx = Size.x * 0.5f, hz = Size.z * 0.5f;
					return new Vector2(Mathf.Clamp(p.x, -hx, hx), Mathf.Clamp(p.y, -hz, hz));
				}
			}
		}

		/// <summary>Even-odd point-in-polygon over the local-XZ outline (x,z = point.x,point.y in the list's plane).</summary>
		private bool PolygonContains(float x, float z)
		{
			var pts = Polygon;
			int n = pts != null ? pts.Count : 0;
			if (n < 3) return false;

			bool inside = false;
			for (int i = 0, j = n - 1; i < n; j = i++)
			{
				Vector2 a = pts[i], b = pts[j];
				if (((a.y > z) != (b.y > z)) &&
				    (x < (b.x - a.x) * (z - a.y) / (b.y - a.y) + a.x))
					inside = !inside;
			}
			return inside;
		}

		private Vector2 ClosestPointOnPolygon(Vector2 p)
		{
			var pts = Polygon;
			int n = pts.Count;
			Vector2 best = pts[0];
			float bestSqr = float.MaxValue;
			for (int i = 0, j = n - 1; i < n; j = i++)
			{
				Vector2 cp = ClosestOnSegment(p, pts[j], pts[i]);
				float sqr = (cp - p).sqrMagnitude;
				if (sqr < bestSqr) { bestSqr = sqr; best = cp; }
			}
			return best;
		}

		private static Vector2 ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b)
		{
			Vector2 ab = b - a;
			float t = Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f);
			return a + Mathf.Clamp01(t) * ab;
		}

		private bool TryRandomInPolygon(System.Random rng, out Vector2 result)
		{
			result = default;
			var pts = Polygon;
			if (pts == null || pts.Count < 3) return false;

			float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
			for (int i = 0; i < pts.Count; i++)
			{
				minX = Mathf.Min(minX, pts[i].x); maxX = Mathf.Max(maxX, pts[i].x);
				minZ = Mathf.Min(minZ, pts[i].y); maxZ = Mathf.Max(maxZ, pts[i].y);
			}

			// Rejection-sample the bounding box; 24 tries handles all but pathologically thin polygons.
			for (int attempt = 0; attempt < 24; attempt++)
			{
				float x = minX + (float)rng.NextDouble() * (maxX - minX);
				float z = minZ + (float)rng.NextDouble() * (maxZ - minZ);
				if (PolygonContains(x, z)) { result = new Vector2(x, z); return true; }
			}
			return false;
		}

		// =========================================================================
		// Gizmos (subclasses pick colors / draw-when; geometry lives here so every shape renders)
		// =========================================================================

		/// <summary>Draws the footprint outline (and a translucent fill for boxes) in the given colors.</summary>
		public void DrawFootprintGizmo(Color fill, Color line)
		{
			Matrix4x4 prev = Gizmos.matrix;
			Gizmos.matrix = transform.localToWorldMatrix;

			switch (Shape)
			{
				case AreaShape.Circle:
					Gizmos.color = line;
					DrawWireCircle(Offset, Radius);
					break;
				case AreaShape.Polygon:
					Gizmos.color = line;
					DrawWirePolygon();
					break;
				default:
					Gizmos.color = fill; Gizmos.DrawCube(Offset, Size);
					Gizmos.color = line; Gizmos.DrawWireCube(Offset, Size);
					break;
			}

			Gizmos.matrix = prev;
		}

		private static void DrawWireCircle(Vector3 center, float radius)
		{
			const int seg = 32;
			Vector3 prev = center + new Vector3(radius, 0f, 0f);
			for (int i = 1; i <= seg; i++)
			{
				float a = (i / (float)seg) * Mathf.PI * 2f;
				Vector3 cur = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
				Gizmos.DrawLine(prev, cur);
				prev = cur;
			}
		}

		private void DrawWirePolygon()
		{
			var pts = Polygon;
			if (pts == null || pts.Count < 2) return;
			for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
			{
				Vector3 a = Offset + new Vector3(pts[j].x, 0f, pts[j].y);
				Vector3 b = Offset + new Vector3(pts[i].x, 0f, pts[i].y);
				Gizmos.DrawLine(a, b);
			}
		}
	}
}
