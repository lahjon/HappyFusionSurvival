using Fusion;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Common.Interactions
{
	/// <summary>
	/// Base class for "stations" — large, world-anchored networked interactables that
	/// open a per-player session UI on interact (crafting bench, computer, research
	/// terminal, ...). Captures the common IInteractable boilerplate so subclasses only
	/// implement <see cref="OnInteract"/>.
	///
	/// Optionally pickupable: if <see cref="IsPickupable"/> is on and <see cref="PickupItem"/>
	/// is assigned, holding Interact for <see cref="PickupHoldSeconds"/> converts the
	/// station into a placeable carried in the player's hands (not the hotbar).
	///
	/// Not for chests/seats/pickups: those have occupancy or authority-transfer rules
	/// that don't fit a "just open a UI" base. See <c>LootContainer</c> / <c>Seat</c>.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	public abstract class InteractableStation : NetworkBehaviour, IInteractable, IPickupableStation
	{
		[Header("Station")]
		public string DisplayName = "Station";

		[Min(0f)] public float InteractRange = 2.5f;

		[Tooltip("Local-space offset of the point used by the scanner for range + line-of-sight. Zero = transform.position.")]
		public Vector3 InteractionPointOffset = Vector3.zero;

		[Header("Pickup")]
		[Tooltip("If true, the player can hold Interact to start carrying this station as a placeable.")]
		public bool IsPickupable = false;

		[Tooltip("Item granted to the player when pickup completes. Must carry a PlaceableCapability. Required when IsPickupable is on.")]
		public ItemData PickupItem;

		[Tooltip("Seconds the player must hold Interact to pick this up.")]
		[Min(0.1f)] public float PickupHoldSeconds = 2f;

		// --- IInteractable ---

		float IInteractable.InteractRange => InteractRange;
		Vector3 IInteractable.InteractionPoint => transform.TransformPoint(InteractionPointOffset);
		public virtual bool CanInteract => true;
		public virtual string LockedReason => string.Empty;

		bool IInteractable.CanInteract => CanInteract;
		string IInteractable.LockedReason => LockedReason;
		string IInteractable.InteractLabel => DisplayName;

		void IInteractable.OnInteract(InteractionScanner scanner) => OnInteract(scanner);

		protected abstract void OnInteract(InteractionScanner scanner);

		// --- IPickupableStation ---

		bool IPickupableStation.IsPickupable => IsPickupable && PickupItem != null;
		ItemData IPickupableStation.AsItem => PickupItem;
		float IPickupableStation.PickupHoldSeconds => PickupHoldSeconds;

		/// <summary>Subclasses override to refuse pickup while a session is open, etc. Empty/null allows it.</summary>
		public virtual string PickupBlockedReason => string.Empty;
		string IPickupableStation.PickupBlockedReason => PickupBlockedReason;

		void IPickupableStation.LocalRequestPickup() => RPC_RequestPickup();

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_RequestPickup(RpcInfo info = default)
		{
			if (Runner == null) return;
			if (!IsPickupable || PickupItem == null) return;
			if (!string.IsNullOrEmpty(PickupBlockedReason)) return;

			var source = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
			var playerObj = Runner.GetPlayerObject(source);
			if (playerObj == null) return;

			if (!IsWithinHostRange(playerObj.transform.position)) return;

			var inv = playerObj.GetComponentInChildren<IPlayerInventory>();
			if (inv == null) return;

			if (!inv.AuthorityStartCarry(PickupItem.Id)) return;

			Runner.Despawn(Object);
		}

		/// <summary>
		/// Host-side range re-validation for any RPC the station kicks off — clients can lie
		/// about being in range, so always confirm. Convention: 1.25× the authored range.
		/// </summary>
		protected bool IsWithinHostRange(Vector3 playerPosition, float multiplier = 1.25f)
		{
			float allowed = InteractRange * multiplier;
			return (transform.position - playerPosition).sqrMagnitude <= allowed * allowed;
		}
	}
}
