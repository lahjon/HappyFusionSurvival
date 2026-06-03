using System.Collections.Generic;
using UnityEngine;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// A reusable, designer-authored pool of weighted items — e.g. a "Weapons" table or a
	/// "Consumables" table. Bundled into a <see cref="LootTableSet"/> for vendors / quest givers,
	/// and (later) directly referenceable by LootContainer / LootDropper. Rolls are delegated to
	/// <see cref="LootRoll"/> so every loot source shares the same weighted-pick math.
	/// </summary>
	[CreateAssetMenu(fileName = "LootTable", menuName = "Inventory/Loot Table", order = 2)]
	public sealed class LootTable : ScriptableObject
	{
		public string DisplayName = "Loot Table";

		[Tooltip("Candidate items. Each roll picks one, weighted by Weight. Entries with Weight <= 0 " +
		         "are only used as a fallback when every weight in the table is zero.")]
		public List<LootEntry> Entries = new();

		/// <summary>Deterministic weighted pick (worldgen RNG). False if the table has no usable item.</summary>
		public bool TryRoll(System.Random rng, out LootEntry entry) =>
			LootRoll.TryPickWeighted(Entries, rng, out entry);
	}
}
