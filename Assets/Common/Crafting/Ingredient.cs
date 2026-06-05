using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Common.Crafting
{
	[System.Serializable]
	public struct Ingredient
	{
		public ItemData Item;
		[Min(1)] public short Count;
	}
}
