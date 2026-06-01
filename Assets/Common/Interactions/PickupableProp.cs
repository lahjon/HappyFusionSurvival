using Fusion;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Common.Interactions
{
	/// <summary>
	/// Minimal networked prop that can be picked up via hold-Interact but has no
	/// tap-interaction UI of its own (e.g. a placed crate that is not a loot container).
	/// For stations that also open a session on tap, use <see cref="InteractableStation"/>;
	/// for chests, <see cref="Starter.Common.Inventory.LootContainer"/> already handles pickup.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	public sealed class PickupableProp : NetworkBehaviour, IInteractable, IPickupableStation
	{
		[Header("Authoring")]
		public string DisplayName = "Prop";
		[Min(0f)] public float InteractRange = 2.5f;
		[Tooltip("Local-space offset of the point used for range + line-of-sight. Zero = transform.position.")]
		public Vector3 InteractionPointOffset = Vector3.zero;

		[Header("Pickup")]
		public bool IsPickupable = true;
		[Tooltip("Item granted to the player when pickup completes. Must carry a PlaceableCapability.")]
		public ItemDefinition PickupItem;
		[Min(0.1f)] public float PickupHoldSeconds = 2f;

		// --- IInteractable ---

		float IInteractable.InteractRange => InteractRange;
		Vector3 IInteractable.InteractionPoint => transform.TransformPoint(InteractionPointOffset);
		bool IInteractable.CanInteract => true;
		string IInteractable.LockedReason => null;

		// Tap is a no-op: the prompt still highlights, the hold path is the only useful action.
		void IInteractable.OnInteract(InteractionScanner scanner) { }

		// --- IPickupableStation ---

		bool IPickupableStation.IsPickupable => IsPickupable && PickupItem != null;
		ItemDefinition IPickupableStation.AsItem => PickupItem;
		float IPickupableStation.PickupHoldSeconds => PickupHoldSeconds;
		string IPickupableStation.PickupBlockedReason => string.Empty;

		void IPickupableStation.LocalRequestPickup() => RPC_RequestPickup();

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_RequestPickup(RpcInfo info = default)
		{
			if (Runner == null) return;
			if (!IsPickupable || PickupItem == null) return;

			var source = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
			var playerObj = Runner.GetPlayerObject(source);
			if (playerObj == null) return;

			float allowed = InteractRange * 1.25f;
			if ((playerObj.transform.position - transform.position).sqrMagnitude > allowed * allowed) return;

			var inv = playerObj.GetComponentInChildren<IPlayerInventory>();
			if (inv == null) return;

			if (!inv.AuthorityStartCarry(PickupItem.Id)) return;

			Runner.Despawn(Object);
		}
	}
}
