using System.Collections.Generic;
using UnityEngine;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// A weighted bundle of <see cref="LootTable"/>s. A vendor (or any spawner) rolls in two steps:
	/// first pick which table to draw from (weighted by <see cref="WeightedTable.Weight"/>), then
	/// roll an item within that table. Lets designers compose, say, a general store from a Weapons
	/// table (low weight) + a Consumables table (high weight) without re-listing items per vendor.
	/// </summary>
	[CreateAssetMenu(fileName = "LootTableSet", menuName = "Inventory/Loot Table Set", order = 3)]
	public sealed class LootTableSet : ScriptableObject
	{
		[System.Serializable]
		public struct WeightedTable
		{
			public LootTable Table;

			[Min(0f)] public float Weight;
		}

		public string DisplayName = "Loot Table Set";

		[Tooltip("Tables to draw from. A roll first selects a table by weight, then an item within it.")]
		public List<WeightedTable> Tables = new();

		/// <summary>
		/// Roll one entry: weighted-pick a table, then weighted-pick an item inside it.
		/// Returns false if no table yields a usable item.
		/// </summary>
		public bool TryRoll(System.Random rng, out LootEntry entry)
		{
			entry = default;
			if (Tables == null || Tables.Count == 0) return false;

			var table = PickTable(rng);
			return table != null && table.TryRoll(rng, out entry);
		}

		private LootTable PickTable(System.Random rng)
		{
			float total = 0f;
			for (int i = 0; i < Tables.Count; i++)
				if (Tables[i].Table != null && Tables[i].Weight > 0f) total += Tables[i].Weight;

			if (total <= 0f)
			{
				// All weights zero / missing — fall back to first assigned table.
				for (int i = 0; i < Tables.Count; i++)
					if (Tables[i].Table != null) return Tables[i].Table;
				return null;
			}

			float pick = rng != null ? (float)(rng.NextDouble() * total) : Random.Range(0f, total);
			float acc = 0f;
			LootTable last = null;
			for (int i = 0; i < Tables.Count; i++)
			{
				if (Tables[i].Table == null || Tables[i].Weight <= 0f) continue;
				acc += Tables[i].Weight;
				last = Tables[i].Table;
				if (pick <= acc) return Tables[i].Table;
			}
			return last;
		}
	}
}
