using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Starter.Shooter.EditorTools
{
	/// <summary>
	/// Scene-view editor for <see cref="WorldArea"/> (and its subclasses <see cref="Zone"/> /
	/// <see cref="NpcMovementArea"/>) polygon footprints. When Shape = Polygon it draws a draggable
	/// handle per vertex, a "+" button on each edge midpoint to insert a vertex, and an "x" to delete
	/// one. Points are edited in world space but stored as local-XZ (relative to Offset), so a moved,
	/// rotated or scaled area keeps its outline. Box/Circle shapes use the default inspector.
	/// </summary>
	[CustomEditor(typeof(WorldArea), editorForChildClasses: true)]
	public sealed class WorldAreaEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			DrawDefaultInspector();

			var area = (WorldArea)target;
			if (area.Shape != WorldArea.AreaShape.Polygon)
			{
				EditorGUILayout.HelpBox("Set Shape to Polygon to edit an outline in the Scene view.", MessageType.None);
				return;
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Polygon tools", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox(
				"Drag the spheres to move vertices. Click a green '+' on an edge to insert a vertex; " +
				"click a red 'x' to delete one (min 3). All editing happens on the area's local XZ plane.",
				MessageType.Info);

			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Seed Square from Size"))
					SeedSquare(area);
				if (GUILayout.Button("Reverse Winding"))
					ReverseWinding(area);
				using (new EditorGUI.DisabledScope(area.Polygon != null && area.Polygon.Count <= 3))
					if (GUILayout.Button("Clear"))
						ClearToTriangle(area);
			}
		}

		private void OnSceneGUI()
		{
			var area = (WorldArea)target;
			if (area.Shape != WorldArea.AreaShape.Polygon) return;

			var pts = area.Polygon;
			if (pts == null) { area.Polygon = pts = new List<Vector2>(); }

			// An empty polygon can't be dragged into existence — give the user a square to start from.
			if (pts.Count == 0)
			{
				Handles.BeginGUI();
				if (GUI.Button(new Rect(10, 10, 200, 24), "Seed square footprint"))
					SeedSquare(area);
				Handles.EndGUI();
				return;
			}

			Transform t = area.transform;
			int deleteIndex = -1;
			int insertAfter = -1;

			// ── Vertex handles ──────────────────────────────────────────────────
			for (int i = 0; i < pts.Count; i++)
			{
				Vector3 world = LocalToWorld(t, area.Offset, pts[i]);
				float size = HandleUtility.GetHandleSize(world);

				EditorGUI.BeginChangeCheck();
				Vector3 moved = Handles.FreeMoveHandle(world, size * 0.08f, Vector3.zero, Handles.SphereHandleCap);
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(area, "Move Polygon Vertex");
					pts[i] = WorldToLocal(t, area.Offset, moved);
					EditorUtility.SetDirty(area);
				}

				// Delete button ('x'), only when removing keeps a valid triangle.
				if (pts.Count > 3)
				{
					Handles.color = new Color(1f, 0.3f, 0.25f, 1f);
					Vector3 delPos = world + Camera.current.transform.right * size * 0.18f
					                       + Camera.current.transform.up * size * 0.18f;
					if (Handles.Button(delPos, Quaternion.identity, size * 0.05f, size * 0.05f, Handles.DotHandleCap))
						deleteIndex = i;
				}
			}

			// ── Edge midpoint insert buttons ('+') ──────────────────────────────
			Handles.color = new Color(0.4f, 1f, 0.5f, 1f);
			for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
			{
				Vector3 a = LocalToWorld(t, area.Offset, pts[j]);
				Vector3 b = LocalToWorld(t, area.Offset, pts[i]);
				Vector3 mid = (a + b) * 0.5f;
				float size = HandleUtility.GetHandleSize(mid);
				if (Handles.Button(mid, Quaternion.identity, size * 0.06f, size * 0.06f, Handles.SphereHandleCap))
					insertAfter = j;
			}

			// Apply structural edits after the loops so indices stay valid.
			if (deleteIndex >= 0)
			{
				Undo.RecordObject(area, "Delete Polygon Vertex");
				pts.RemoveAt(deleteIndex);
				EditorUtility.SetDirty(area);
			}
			else if (insertAfter >= 0)
			{
				Undo.RecordObject(area, "Insert Polygon Vertex");
				int next = (insertAfter + 1) % pts.Count;
				pts.Insert(insertAfter + 1, (pts[insertAfter] + pts[next]) * 0.5f);
				EditorUtility.SetDirty(area);
			}
		}

		// ── Helpers ─────────────────────────────────────────────────────────────

		private static Vector3 LocalToWorld(Transform t, Vector3 offset, Vector2 p)
			=> t.TransformPoint(new Vector3(p.x, 0f, p.y) + offset);

		private static Vector2 WorldToLocal(Transform t, Vector3 offset, Vector3 world)
		{
			Vector3 local = t.InverseTransformPoint(world) - offset;
			return new Vector2(local.x, local.z);
		}

		private static void SeedSquare(WorldArea area)
		{
			Undo.RecordObject(area, "Seed Polygon Square");
			float hx = Mathf.Max(0.5f, area.Size.x * 0.5f);
			float hz = Mathf.Max(0.5f, area.Size.z * 0.5f);
			area.Polygon = new List<Vector2>
			{
				new Vector2(-hx, -hz), new Vector2( hx, -hz),
				new Vector2( hx,  hz), new Vector2(-hx,  hz),
			};
			EditorUtility.SetDirty(area);
		}

		private static void ReverseWinding(WorldArea area)
		{
			if (area.Polygon == null || area.Polygon.Count < 2) return;
			Undo.RecordObject(area, "Reverse Polygon Winding");
			area.Polygon.Reverse();
			EditorUtility.SetDirty(area);
		}

		private static void ClearToTriangle(WorldArea area)
		{
			Undo.RecordObject(area, "Clear Polygon");
			float hx = Mathf.Max(0.5f, area.Size.x * 0.5f);
			float hz = Mathf.Max(0.5f, area.Size.z * 0.5f);
			area.Polygon = new List<Vector2>
			{
				new Vector2(0f, hz), new Vector2(hx, -hz), new Vector2(-hx, -hz),
			};
			EditorUtility.SetDirty(area);
		}
	}
}
