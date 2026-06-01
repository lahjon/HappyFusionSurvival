using UnityEngine;
using UnityEngine.AI;

namespace Starter.Shooter
{
	/// <summary>
	/// Box volume that bounds where an NPC is allowed to move. Scene-placed and
	/// local-only — only the state authority's AI reads it (proxies don't simulate).
	/// The box is centered on this transform and oriented by it; <see cref="Size"/>
	/// is in local space. Y is ignored for containment so ground NPCs on uneven
	/// terrain still register as "inside".
	/// </summary>
	public sealed class NpcMovementArea : MonoBehaviour
	{
		[Tooltip("Box dimensions in local space (X/Y/Z). Y only affects the gizmo; containment uses X/Z.")]
		public Vector3 Size = new Vector3(20f, 4f, 20f);

		[Tooltip("Vertical search distance used when snapping a random point down onto the navmesh.")]
		[Min(0.1f)] public float NavSampleHeight = 4f;

		public Vector3 Center => transform.position;

		/// <summary>True if <paramref name="worldPoint"/> is within the box footprint (X/Z only).</summary>
		public bool Contains(Vector3 worldPoint)
		{
			Vector3 local = transform.InverseTransformPoint(worldPoint);
			Vector3 half = Size * 0.5f;
			return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z;
		}

		/// <summary>Clamps <paramref name="worldPoint"/> into the box footprint and returns it in world space.</summary>
		public Vector3 ClampInside(Vector3 worldPoint)
		{
			Vector3 local = transform.InverseTransformPoint(worldPoint);
			Vector3 half = Size * 0.5f;
			local.x = Mathf.Clamp(local.x, -half.x, half.x);
			local.z = Mathf.Clamp(local.z, -half.z, half.z);
			return transform.TransformPoint(local);
		}

		/// <summary>
		/// Picks a random point inside the box (using the caller's per-instance RNG so NPCs
		/// roam independently) and snaps it onto the navmesh. Returns false when no navmesh is
		/// found near the sample (caller should retry shortly).
		/// </summary>
		public bool TryRandomPoint(System.Random rng, out Vector3 result)
		{
			Vector3 half = Size * 0.5f;
			float rx = ((float)rng.NextDouble() * 2f - 1f) * half.x;
			float rz = ((float)rng.NextDouble() * 2f - 1f) * half.z;
			Vector3 local = new Vector3(rx, 0f, rz);
			Vector3 candidate = transform.TransformPoint(local);
			if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, NavSampleHeight, NavMesh.AllAreas))
			{
				result = hit.position;
				return true;
			}
			result = default;
			return false;
		}

		private void OnDrawGizmosSelected()
		{
			Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
			Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
			Gizmos.DrawCube(Vector3.zero, Size);
			Gizmos.color = new Color(0.2f, 0.8f, 1f, 1f);
			Gizmos.DrawWireCube(Vector3.zero, Size);
		}
	}
}
