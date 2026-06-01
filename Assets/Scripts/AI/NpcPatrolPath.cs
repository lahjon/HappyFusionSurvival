using System.Collections.Generic;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Ordered list of patrol waypoints for a Patrol <see cref="NpcAgent"/>. Scene-placed
	/// and local-only (state authority reads it). Either drag waypoint transforms into
	/// <see cref="Waypoints"/>, or leave it empty and parent the waypoints as direct
	/// children — they are then used in hierarchy order.
	/// </summary>
	public sealed class NpcPatrolPath : MonoBehaviour
	{
		[Tooltip("Ordered patrol waypoints. If empty, direct child transforms are used in hierarchy order.")]
		public List<Transform> Waypoints = new List<Transform>();

		private List<Transform> _childCache;

		private List<Transform> Resolved
		{
			get
			{
				if (Waypoints != null && Waypoints.Count > 0) return Waypoints;
				_childCache ??= CollectChildren();
				return _childCache;
			}
		}

		public int Count => Resolved.Count;

		private List<Transform> CollectChildren()
		{
			var list = new List<Transform>(transform.childCount);
			foreach (Transform child in transform) list.Add(child);
			return list;
		}

		public bool TryGetPoint(int index, out Vector3 position)
		{
			var wp = Resolved;
			if (index < 0 || index >= wp.Count || wp[index] == null)
			{
				position = default;
				return false;
			}
			position = wp[index].position;
			return true;
		}

		/// <summary>
		/// Returns the next waypoint index from <paramref name="current"/> for the given mode.
		/// For PingPong, <paramref name="dir"/> tracks travel direction (+1/-1) and is flipped at the ends.
		/// </summary>
		public int NextIndex(int current, PatrolMode mode, ref int dir)
		{
			int count = Count;
			if (count <= 1) return 0;

			switch (mode)
			{
				case PatrolMode.Loop:
					return (current + 1) % count;

				case PatrolMode.Once:
					return Mathf.Min(current + 1, count - 1);

				case PatrolMode.PingPong:
					if (current + dir >= count || current + dir < 0) dir = -dir;
					return Mathf.Clamp(current + dir, 0, count - 1);

				default:
					return 0;
			}
		}

		/// <summary>Index of the waypoint closest to <paramref name="to"/> (used to resume a patrol after a chase).</summary>
		public int ClosestIndex(Vector3 to)
		{
			var wp = Resolved;
			int best = 0;
			float bestSqr = float.MaxValue;
			for (int i = 0; i < wp.Count; i++)
			{
				if (wp[i] == null) continue;
				float sqr = (wp[i].position - to).sqrMagnitude;
				if (sqr < bestSqr) { bestSqr = sqr; best = i; }
			}
			return best;
		}

		private void OnDrawGizmos()
		{
			// Read children live in edit mode so the path updates as waypoints are added/moved.
			var wp = (Waypoints != null && Waypoints.Count > 0) ? Waypoints : CollectChildren();
			Gizmos.color = Color.green;
			for (int i = 0; i < wp.Count; i++)
			{
				if (wp[i] == null) continue;
				Gizmos.DrawWireSphere(wp[i].position, 0.3f);
				int next = (i + 1) % wp.Count;
				if (wp.Count > 1 && wp[next] != null) Gizmos.DrawLine(wp[i].position, wp[next].position);
			}
		}
	}
}
