using System.Collections.Generic;
using Fusion;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Reusable loot-drop emitter. Drop this on any <see cref="NetworkObject"/> (breakable pot,
	/// crate, chest, dead AI) and call <see cref="DropLoot"/> on the state authority to scatter a
	/// rolled set of world pickups on the ground.
	///
	/// Each drop is a real <see cref="PickupableItem"/> spawned via <c>Runner.Spawn</c> and tossed
	/// with a small random impulse (reusing <see cref="PickupableItem.Throw"/>), so it arcs out,
	/// settles, and is immediately interactable/networked like any other dropped stack. The roll
	/// uses the same weighted <see cref="LootEntry"/> table shape as PickupableItem / LootContainer.
	///
	/// State-authority only spawns; the resulting pickups replicate themselves, so callers don't
	/// need to do anything beyond invoking <see cref="DropLoot"/> on the host.
	/// </summary>
	public sealed class LootDropper : NetworkBehaviour
	{
		[Header("Loot Table")]
		[Tooltip("Candidate items. Each DropLoot() rolls between Min and Max picks, weighted by Weight " +
		         "(Weight <= 0 entries are only used as a fallback when every weight is zero).")]
		[SerializeField] private List<LootEntry> _lootTable = new();
		[Tooltip("Minimum number of pickups spawned per DropLoot() call.")]
		[SerializeField, Min(0)] private int _minDrops = 1;
		[Tooltip("Maximum number of pickups spawned per DropLoot() call (inclusive).")]
		[SerializeField, Min(0)] private int _maxDrops = 3;

		[Header("Spawn")]
		[Tooltip("Shared world-pickup prefab (NetworkObject + PickupableItem) used for any loot item that " +
		         "has no bespoke WorldPrefab override on its ItemData. Wire Pickup_Generic here.")]
		[SerializeField] private GameObject _genericPickupPrefab;
		[Tooltip("Vertical offset (m) above this object's origin where loot spawns before being tossed.")]
		[SerializeField] private float _spawnHeight = 0.3f;
		[Tooltip("Horizontal scatter speed (m/s) applied to each pickup so the loot spreads out.")]
		[SerializeField] private float _scatterSpeed = 2f;
		[Tooltip("Upward speed (m/s) applied to each pickup so it pops up before settling.")]
		[SerializeField] private float _popSpeed = 2.5f;
		[Tooltip("Seconds each dropped pickup stays non-interactable after spawning (prevents instant re-grab).")]
		[SerializeField] private float _interactionLock = 0.4f;

		/// <summary>
		/// State-authority only. Roll the loot table and spawn the resulting pickups, each tossed out
		/// with a small random arc. A no-op on clients and when the table is empty, so it is safe to
		/// call unconditionally from any break/death hook.
		/// </summary>
		public void DropLoot()
		{
			if (Object == null || Object.HasStateAuthority == false) return;
			if (_lootTable == null || _lootTable.Count == 0) return;
			if (Runner == null || ItemDatabase.Instance == null) return;

			int count = Mathf.Max(_minDrops, 0);
			if (_maxDrops > _minDrops)
				count = Random.Range(_minDrops, _maxDrops + 1);

			for (int i = 0; i < count; i++)
			{
				if (!LootRoll.TryPickWeighted(_lootTable, out var entry) || entry.Item == null) continue;

				var prefab = ResolvePickupPrefab(entry.Item);
				if (prefab == null) continue;

				Vector3 pos = transform.position + Vector3.up * _spawnHeight;
				var spawned = Runner.Spawn(prefab, pos, Quaternion.identity);
				if (spawned == null) continue;
				if (spawned.TryGetComponent<PickupableItem>(out var pi) == false)
				{
					Runner.Despawn(spawned);
					continue;
				}

				pi.Initialize(entry.Item.Id, entry.Count > 0 ? entry.Count : (short)1);

				// Random horizontal direction + upward pop, so a stack of drops fans out instead of
				// stacking on one spot. Reuses PickupableItem's throw arc (host-only Rigidbody).
				Vector2 dir = Random.insideUnitCircle;
				if (dir.sqrMagnitude < 1e-4f) dir = Vector2.up;
				Vector3 velocity = new Vector3(dir.x, 0f, dir.y).normalized * _scatterSpeed + Vector3.up * _popSpeed;
				pi.Throw(velocity, _interactionLock);
			}
		}

		/// <summary>Item's bespoke WorldPrefab if it sets one, else the shared generic pickup. Null if neither resolves.</summary>
		private GameObject ResolvePickupPrefab(ItemData def)
		{
			if (def == null) return null;

			var prefab = def.Visual.WorldPrefab != null ? def.Visual.WorldPrefab : _genericPickupPrefab;
			if (prefab == null)
			{
				Debug.LogWarning($"[LootDropper] '{name}': loot item '{def.DisplayName}' has no WorldPrefab and no " +
				                 "generic pickup prefab is assigned — skipped.", this);
				return null;
			}
			if (prefab.GetComponent<NetworkObject>() == null)
			{
				Debug.LogWarning($"[LootDropper] '{name}': pickup prefab '{prefab.name}' has no NetworkObject — skipped.", this);
				return null;
			}
			return prefab;
		}
	}
}
