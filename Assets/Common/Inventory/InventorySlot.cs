using Fusion;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// Network-friendly cell for a single hotbar slot. ItemId == 0 means empty.
	/// </summary>
	public struct InventorySlot : INetworkStruct
	{
		public short ItemId;
		public short Count;
		/// <summary>Rounds currently chambered in this slot's weapon (magazine-fed guns only; 0 for
		/// everything else). Rides the networked slot array so the magazine count replicates and the
		/// per-shot decrement predicts on the input authority. Only meaningful for non-stacking weapons
		/// (MaxStack == 1), which never merge, so there's no ambiguity about which gun's magazine it is.</summary>
		public short Loaded;

		public bool IsEmpty => ItemId == 0 || Count <= 0;

		public static InventorySlot Empty => default;
	}
}
