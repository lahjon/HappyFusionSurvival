using System.Collections.Generic;
using UnityEngine;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// Single source of the weighted-random pick math over a list of <see cref="LootEntry"/>.
	/// Previously duplicated across PickupableItem / LootContainer / LootDropper — they now all
	/// delegate here. Two overloads: a deterministic one taking a <see cref="System.Random"/>
	/// (worldgen rolls seeded by <see cref="WorldGen.RngFor"/>) and a non-deterministic one using
	/// <see cref="UnityEngine.Random"/> (event-driven drops). Both pick among positive-weight
	/// entries, and fall back to the first non-null entry when every weight is zero.
	/// </summary>
	public static class LootRoll
	{
		/// <summary>
		/// Deterministic weighted pick. Returns false (and <paramref name="result"/> = default) only
		/// when the list has no non-null item at all.
		/// </summary>
		public static bool TryPickWeighted(IReadOnlyList<LootEntry> entries, System.Random rng, out LootEntry result)
		{
			result = default;
			if (entries == null || entries.Count == 0) return false;

			float total = SumWeights(entries);
			if (total <= 0f) return TryFirstNonNull(entries, out result);

			float pick = rng != null ? (float)(rng.NextDouble() * total) : Random.Range(0f, total);
			return PickAt(entries, pick, out result);
		}

		/// <summary>
		/// Non-deterministic weighted pick using <see cref="UnityEngine.Random"/>. For event-driven
		/// rolls (a break/death) where only the host rolls and the result replicates.
		/// </summary>
		public static bool TryPickWeighted(IReadOnlyList<LootEntry> entries, out LootEntry result)
		{
			result = default;
			if (entries == null || entries.Count == 0) return false;

			float total = SumWeights(entries);
			if (total <= 0f) return TryFirstNonNull(entries, out result);

			return PickAt(entries, Random.Range(0f, total), out result);
		}

		private static float SumWeights(IReadOnlyList<LootEntry> entries)
		{
			float total = 0f;
			for (int i = 0; i < entries.Count; i++)
				if (entries[i].Item != null && entries[i].Weight > 0f) total += entries[i].Weight;
			return total;
		}

		private static bool PickAt(IReadOnlyList<LootEntry> entries, float pick, out LootEntry result)
		{
			float acc = 0f;
			LootEntry last = default;
			bool any = false;
			for (int i = 0; i < entries.Count; i++)
			{
				if (entries[i].Item == null || entries[i].Weight <= 0f) continue;
				acc += entries[i].Weight;
				last = entries[i];
				any = true;
				if (pick <= acc)
				{
					result = entries[i];
					return true;
				}
			}
			result = last;
			return any;
		}

		private static bool TryFirstNonNull(IReadOnlyList<LootEntry> entries, out LootEntry result)
		{
			for (int i = 0; i < entries.Count; i++)
			{
				if (entries[i].Item != null)
				{
					result = entries[i];
					return true;
				}
			}
			result = default;
			return false;
		}
	}
}
