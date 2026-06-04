using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only orchestrator for "having a shopkeeper open". See <see cref="InteractableSession{TStation}"/>
	/// for the shared open/close/auto-close lifecycle — this subclass adds the buy/sell forwarders, its own
	/// "is any open" flag, and the NPC attend hooks.
	/// </summary>
	public sealed class ShopSession : InteractableSession<Shopkeeper>
	{
		/// <summary>True while ANY local ShopSession has an open shopkeeper. Polled by UI gates to suppress menu toggles while shopping.</summary>
		public static bool IsAnyOpen { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics() => IsAnyOpen = false;

		protected override string Label => "Shop";
		protected override void SetAnyOpen(bool value) => IsAnyOpen = value;

		// Tell the NPC (if it's an NpcAgent) to stop and face us while we shop.
		protected override void OnOpened(Shopkeeper shopkeeper) => shopkeeper.GetComponent<NpcAgent>()?.LocalBeginAttending();
		protected override void OnClosed(Shopkeeper shopkeeper) => shopkeeper.GetComponent<NpcAgent>()?.LocalEndAttending();

		/// <summary>Forward a buy request to the open shopkeeper's state authority. UI should pre-gate via stock / money / inventory room.</summary>
		public void RequestBuy(int offerIndex)
		{
			if (Current == null) return;
			Current.RPC_RequestBuy(offerIndex);
		}

		/// <summary>Forward a sell request for the local player's hotbar slot. UI should pre-gate via slot contents / shop sell price.</summary>
		public void RequestSell(int slotIndex)
		{
			if (Current == null) return;
			Current.RPC_RequestSell(slotIndex);
		}
	}
}
