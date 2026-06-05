using System;
using UnityEngine;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// One candidate entry for a procedural loot roll. Inline list on
	/// PickupableItem / LootContainer; the host rolls weighted-random across these
	/// at spawn time and writes the resolved item to the [Networked] slot.
	/// </summary>
	[Serializable]
	public struct LootEntry
	{
		public ItemData Item;

		[Min(0f)]
		public float Weight;

		[Min(1)]
		public short Count;

		public static LootEntry Default(ItemData item) =>
			new() { Item = item, Weight = 1f, Count = 1 };
	}
}
