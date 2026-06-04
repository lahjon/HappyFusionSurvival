using UnityEngine;
using UnityEngine.AI;

namespace Starter.Shooter
{
	/// <summary>
	/// Bounds where an NPC is allowed to move. Scene-placed and local-only — only the state authority's AI
	/// reads it (proxies don't simulate). The footprint (Box / Circle / Polygon), containment and clamping
	/// all come from <see cref="WorldArea"/>; this class only adds navmesh-aware random sampling for roaming.
	/// </summary>
	public sealed class NpcMovementArea : WorldArea
	{
		[Header("NPC roam")]
		[Tooltip("Vertical search distance used when snapping a random point down onto the navmesh.")]
		[Min(0.1f)] public float NavSampleHeight = 4f;

		/// <summary>
		/// Picks a random point inside the footprint (using the caller's per-instance RNG so NPCs roam
		/// independently) and snaps it onto the navmesh. Returns false when no navmesh is found near the
		/// sample (caller should retry shortly).
		/// </summary>
		public bool TryRandomPoint(System.Random rng, out Vector3 result)
		{
			if (TryRandomLocalPoint(rng, out Vector3 candidate) &&
			    NavMesh.SamplePosition(candidate, out NavMeshHit hit, NavSampleHeight, NavMesh.AllAreas))
			{
				result = hit.position;
				return true;
			}
			result = default;
			return false;
		}

		private void OnDrawGizmosSelected()
		{
			DrawFootprintGizmo(new Color(0.2f, 0.8f, 1f, 0.25f), new Color(0.2f, 0.8f, 1f, 1f));
		}
	}
}
