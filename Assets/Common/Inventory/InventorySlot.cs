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

		public bool IsEmpty => ItemId == 0 || Count <= 0;

		public static InventorySlot Empty => default;
	}
}
