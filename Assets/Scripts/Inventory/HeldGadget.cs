using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Abstract base for a handheld "gadget" device (radar/scanner, motion tracker, drone controller,
	/// …). A gadget is a local-only visual+logic module that rides on the held hand instance: a
	/// <see cref="GadgetCapability"/> on the item asset attaches one via <c>AddComponent</c> at equip
	/// (see <c>Inventory.RefreshHeldItem</c>) and Unity destroys it when the slot is deselected, so the
	/// "active only while held" rule falls out for free — exactly like the old bespoke HandScanner, but
	/// without a bespoke HandPrefab.
	///
	/// This base owns the parts every gadget shares:
	///  • owner resolution (walk up to the <see cref="Inventory"/>/<see cref="Player"/> that holds us),
	///  • the per-viewer vs. owner split — <see cref="OnRender"/> runs on every peer's instance (cosmetic
	///    sweep/animation), <see cref="OnOwnerTick"/> only on the input-authority's instance (the actual
	///    scan), matching the "you only know what your own device saw" rule,
	///  • activation lifecycle so heavy setup runs after tuning is applied, not in Awake.
	///
	/// Concrete gadgets (e.g. <see cref="HandScanner"/>) override the hooks and supply their own data
	/// through the matching <see cref="GadgetCapability"/> subclass. Adding a new gadget is one capability
	/// + one <c>HeldGadget</c> subclass — no prefab, no Inventory edit.
	/// </summary>
	public abstract class HeldGadget : MonoBehaviour
	{
		/// <summary>The player holding this gadget, or null if the owner couldn't be resolved.</summary>
		protected Player OwnerPlayer { get; private set; }

		/// <summary>The inventory that spawned the hand instance this gadget rides on.</summary>
		protected Inventory OwnerInventory { get; private set; }

		/// <summary>True only on the local player's own instance — the peer that should run the scan logic.</summary>
		protected bool OwnerHasInputAuthority => OwnerInventory != null && OwnerInventory.HasInputAuthority;

		private bool _active;

		/// <summary>
		/// Called by the <see cref="GadgetCapability"/> factory right after the concrete subclass has
		/// copied its tuning out of the capability. Resolves the owner and runs the subclass setup, then
		/// lets <see cref="Update"/> begin driving the gadget. Kept out of Awake so configuration is in
		/// place before any visuals are built.
		/// </summary>
		protected void Activate()
		{
			ResolveOwner();
			OnActivated();
			_active = true;
		}

		// Walk up parents to find the Inventory that instantiated our hand instance. The hand lives under
		// HandAnchor → CameraHandle → CameraPivot → Player; Inventory is on the Player root.
		private void ResolveOwner()
		{
			var t = transform;
			while (t != null)
			{
				if (t.TryGetComponent<Inventory>(out var inv))
				{
					OwnerInventory = inv;
					OwnerPlayer = inv.GetComponent<Player>();
					return;
				}
				t = t.parent;
			}
		}

		private void Update()
		{
			if (_active == false) return;

			OnRender();

			if (OwnerHasInputAuthority)
				OnOwnerTick();
		}

		/// <summary>One-time setup after tuning is applied and the owner is resolved (build visuals here).</summary>
		protected virtual void OnActivated() { }

		/// <summary>Per-frame cosmetic update that runs on every viewer's instance (sweep, fades, animation).</summary>
		protected virtual void OnRender() { }

		/// <summary>Per-frame logic that runs only on the holding player's own instance (the scan/ping work).</summary>
		protected virtual void OnOwnerTick() { }

		/// <summary>
		/// Local primary-use hook: called once on the holding player's own instance when LMB is pressed while
		/// this gadget is held (routed from <c>Player.ProcessFireInput</c> via the inventory, so it shares the
		/// single Fire input authority — no parallel input read). Use for purely-local effects: an active radar
		/// ping, a flashlight toggle, a local UI readout. Networked effects (movement, spawning a NetworkObject)
		/// must NOT live here — route them through the networked tick like the grapple does on the player.
		/// Only fires when the gadget's <c>GadgetCapability.UsesPrimary</c> is true. Default no-op.
		/// </summary>
		public virtual void OnUsePressed() { }
	}
}
