using Fusion;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// A harvestable world resource (tree → Wood, rock → Stone, ...). Reuses the proven Health + Breakable
	/// pattern: the sibling <see cref="Health"/> doubles as the remaining-hits counter (one HP = one chop),
	/// and the optional sibling <see cref="Breakable"/> handles the replicated shatter VFX + despawn when the
	/// node is exhausted.
	///
	/// A node only reacts to the correct tool. It is NOT damaged through the generic combat path — <see
	/// cref="OverlapAction"/> recognizes a <c>ResourceNode</c> in its swing and routes the hit to
	/// <see cref="TryHarvest"/> with the swinger's tool tag instead of calling <c>Health.TakeHit</c>. Hitting
	/// it with the wrong tool, fists, or a ranged weapon does nothing.
	///
	/// Authority: harvest mutations (Health, yield, despawn) run on the state authority only. The chip/shake
	/// impact FX ride for free on Health's replicated impact counter, since each chop calls Health.TakeHit(1).
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	[RequireComponent(typeof(Health))]
	public sealed class ResourceNode : NetworkBehaviour
	{
		[Header("Harvest Rules")]
		[Tooltip("Only a tool carrying this ToolType (via its ToolCapability) can harvest this node. " +
		         "Required — a node with no tool can never be harvested.")]
		[SerializeField] private ToolTypeTag _requiredTool;
		[Tooltip("The resource item granted by harvesting (e.g. Wood). Must be registered in the ItemDatabase.")]
		[SerializeField] private ItemData _resourceItem;

		[Header("Yield")]
		[Tooltip("Resource units granted to the harvester on each correct hit (per-hit yield). " +
		         "Total hits to exhaust the node = the sibling Health.InitialHealth.")]
		[SerializeField, Min(0)] private int _yieldPerHit = 1;
		[Tooltip("Extra resource units granted to the harvester when the final hit exhausts the node " +
		         "(final loot bonus). 0 = no bonus.")]
		[SerializeField, Min(0)] private int _bonusOnDeplete = 0;

		[Header("Overflow Drop")]
		[Tooltip("Shared world-pickup prefab (NetworkObject + PickupableItem) used to drop yield that does " +
		         "not fit in the harvester's inventory. Wire Pickup_Generic. Items with a bespoke WorldPrefab " +
		         "use that instead. Leave null to silently discard overflow.")]
		[SerializeField] private GameObject _genericPickupPrefab;
		[Tooltip("Vertical offset (m) above the node where overflow pickups spawn before settling.")]
		[SerializeField] private float _dropHeight = 0.5f;

		private Health _health;

		public override void Spawned()
		{
			_health = GetComponent<Health>();
		}

		/// <summary>
		/// Attempt one chop with <paramref name="tool"/>. Returns true only when the swing actually
		/// harvested (correct tool + node still alive) — the caller (<see cref="OverlapAction"/>) uses the
		/// result to drive swing/impact feedback. State-authority only; a no-op (returns false) on clients,
		/// so the routing in OverlapAction must gate on IsStateAuthority before calling.
		/// </summary>
		public bool TryHarvest(ToolTypeTag tool, PlayerRef harvester)
		{
			if (Object == null || Object.HasStateAuthority == false) return false;
			// Wrong tool / no tool (fists, ranged, mismatched tool) does nothing at all.
			if (tool == null || tool != _requiredTool) return false;
			if (_health == null || _health.IsAlive == false) return false;

			// One chop = one HP. Drives the replicated impact FX (shake / chips) on every peer.
			_health.TakeHit(1);

			GrantYield(harvester, _yieldPerHit);

			// Exhausted by this chop → final loot bonus, then consume the node (one-shot).
			if (_health.IsAlive == false)
			{
				GrantYield(harvester, _bonusOnDeplete);
				Deplete();
			}

			return true;
		}

		/// <summary>State-authority only. Add <paramref name="amount"/> of the resource to the harvester's
		/// inventory; any that doesn't fit is dropped as world pickups at the node.</summary>
		private void GrantYield(PlayerRef harvester, int amount)
		{
			if (amount <= 0 || _resourceItem == null || _resourceItem.Id == 0) return;

			short remaining = (short)amount;

			var playerObj = Runner != null ? Runner.GetPlayerObject(harvester) : null;
			if (playerObj != null && playerObj.TryGetComponent<Inventory>(out var inv))
				remaining = inv.TryAdd(_resourceItem.Id, remaining);

			if (remaining > 0)
				DropOverflow(_resourceItem, remaining);
		}

		/// <summary>State-authority only. Spawn a single world pickup carrying the leftover resource.</summary>
		private void DropOverflow(ItemData item, short count)
		{
			if (count <= 0 || Runner == null) return;

			var prefab = item.Visual.WorldPrefab != null ? item.Visual.WorldPrefab : _genericPickupPrefab;
			if (prefab == null || prefab.GetComponent<NetworkObject>() == null) return;

			Vector3 pos = transform.position + Vector3.up * _dropHeight;
			var spawned = Runner.Spawn(prefab, pos, Quaternion.identity);
			if (spawned == null) return;
			if (spawned.TryGetComponent<PickupableItem>(out var pi) == false)
			{
				Runner.Despawn(spawned);
				return;
			}

			pi.Initialize(item.Id, count);
			pi.Throw(Vector3.up * 1.5f, 0.4f);
		}

		/// <summary>State-authority only. Consume the node: shatter + despawn via the sibling Breakable
		/// (replicated VFX) if present, else despawn directly.</summary>
		private void Deplete()
		{
			if (TryGetComponent<Breakable>(out var breakable))
				breakable.Break();
			else
				Runner.Despawn(Object);
		}
	}
}
