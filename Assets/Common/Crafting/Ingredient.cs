using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Common.Crafting
{
	[System.Serializable]
	public struct Ingredient
	{
		public ItemDefinition Item;
		[Min(1)] public short Count;
	}
}
