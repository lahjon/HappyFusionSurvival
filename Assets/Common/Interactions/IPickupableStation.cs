using Starter.Common.Inventory;

namespace Starter.Common.Interactions
{
	/// <summary>
	/// Mixin capability for an <see cref="IInteractable"/> that can be picked up into
	/// the player's inventory by holding the Interact key. Implemented alongside
	/// <see cref="IInteractable"/>; the <see cref="InteractionScanner"/> looks for both
	/// on the same component.
	///
	/// Tap Interact still routes through <see cref="IInteractable.OnInteract"/>; the
	/// hold path is orthogonal so a crate can both open its loot UI (tap) and be picked
	/// up (hold) without bespoke wiring per type.
	/// </summary>
	public interface IPickupableStation
	{
		/// <summary>Author-time gate. False disables hold-to-pickup entirely.</summary>
		bool IsPickupable { get; }

		/// <summary>Item granted to the player when the pickup completes. Must carry a
		/// <see cref="PlaceableCapability"/> so the picked-up object can be re-placed via the
		/// same ghost-preview flow.</summary>
		ItemData AsItem { get; }

		/// <summary>Seconds the player must hold Interact to convert this object.</summary>
		float PickupHoldSeconds { get; }

		/// <summary>
		/// Non-empty reason that pickup is currently refused (e.g. "Empty first" when a
		/// crate still has loot, "In use" when a session is open). Empty/null means
		/// pickup is allowed. The scanner surfaces this as a toast on hold-start.
		/// </summary>
		string PickupBlockedReason { get; }

		/// <summary>
		/// Local-side entry called by the scanner once the hold timer completes. The
		/// implementer is expected to fire its own state-authority RPC which then
		/// re-validates range + blocked-reason, grants <see cref="AsItem"/> to the
		/// requester's inventory, and despawns the object.
		/// </summary>
		void LocalRequestPickup();
	}
}
