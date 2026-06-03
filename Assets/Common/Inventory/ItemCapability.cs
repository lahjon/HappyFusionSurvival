using System;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// A composable behavior module attached to an <see cref="ItemDefinition"/> through its
	/// <c>[SerializeReference] Capabilities</c> list. Each subclass adds the data + behavior for
	/// one facet of an item (weapon, placeable, consumable, ...). An item gains a facet by adding
	/// the matching capability, and facets compose freely on a single item asset — a weapon that
	/// is also a consumable is just an item carrying both capabilities.
	///
	/// Capabilities are stateless authoring data living on a shared ScriptableObject. They must
	/// NOT hold per-instance runtime or networked state — that belongs on the per-actor
	/// NetworkBehaviours (ActionInvoker, Inventory), exactly as <c>CombatAction</c> does.
	/// </summary>
	[Serializable]
	public abstract class ItemCapability
	{
		/// <summary>
		/// Initial <c>InventorySlot.Loaded</c> for a fresh unit carrying this capability — e.g. a gadget's
		/// charge count. 0 means "no per-slot load". <see cref="ItemDefinition.InitialLoaded"/> takes the max
		/// across an item's capabilities and seeds the slot with it on add. Weapons deliberately stay 0 here:
		/// magazines are seeded explicitly at spawn and refilled by the reload cycle, so picked-up guns arrive
		/// empty by design.
		/// </summary>
		public virtual short InitialLoaded => 0;
	}
}
