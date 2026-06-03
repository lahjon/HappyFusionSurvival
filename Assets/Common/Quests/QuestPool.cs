using System.Collections.Generic;
using UnityEngine;

namespace Starter.Common.Quests
{
	/// <summary>
	/// A weighted pool of pre-authored <see cref="QuestDefinition"/>s — the quest analogue of
	/// <see cref="Starter.Common.Inventory.LootTableSet"/>. A quest giver rolls a handful of distinct
	/// quests from this pool at spawn. Quests stay fully authored (wording, requested items, rewards);
	/// only the chosen <see cref="QuestDefinition.Id"/>s are networked, resolved back via
	/// <see cref="QuestDatabase"/>.
	/// </summary>
	[CreateAssetMenu(fileName = "QuestPool", menuName = "Quests/Quest Pool", order = 2)]
	public sealed class QuestPool : ScriptableObject
	{
		[System.Serializable]
		public struct WeightedQuest
		{
			public QuestDefinition Quest;

			[Min(0f)] public float Weight;
		}

		public string DisplayName = "Quest Pool";

		[Tooltip("Candidate quests. A giver rolls distinct quests weighted by Weight. Entries with " +
		         "Weight <= 0 are only used as a fallback when every weight is zero.")]
		public List<WeightedQuest> Quests = new();

		/// <summary>
		/// Roll up to <paramref name="count"/> distinct quests into <paramref name="outQuests"/>
		/// (cleared first). Stops early if the pool runs out of unique candidates. Returns the
		/// number actually rolled.
		/// </summary>
		public int RollUnique(System.Random rng, int count, List<QuestDefinition> outQuests)
		{
			if (outQuests == null) return 0;
			outQuests.Clear();
			if (Quests == null || Quests.Count == 0 || count <= 0) return 0;

			// Work on a local copy of indices so we can remove picked entries without mutating the asset.
			var pool = new List<int>(Quests.Count);
			for (int i = 0; i < Quests.Count; i++)
				if (Quests[i].Quest != null) pool.Add(i);

			while (outQuests.Count < count && pool.Count > 0)
			{
				int chosen = PickIndex(pool, rng);
				if (chosen < 0) break;

				var def = Quests[pool[chosen]].Quest;
				pool.RemoveAt(chosen);

				if (def != null && !outQuests.Contains(def))
					outQuests.Add(def);
			}

			return outQuests.Count;
		}

		/// <summary>Returns the position within <paramref name="pool"/> (not the Quests index) to remove.</summary>
		private int PickIndex(List<int> pool, System.Random rng)
		{
			float total = 0f;
			for (int i = 0; i < pool.Count; i++)
			{
				float w = Quests[pool[i]].Weight;
				if (w > 0f) total += w;
			}

			if (total <= 0f) return pool.Count > 0 ? 0 : -1;

			float pick = rng != null ? (float)(rng.NextDouble() * total) : Random.Range(0f, total);
			float acc = 0f;
			int last = -1;
			for (int i = 0; i < pool.Count; i++)
			{
				float w = Quests[pool[i]].Weight;
				if (w <= 0f) continue;
				acc += w;
				last = i;
				if (pick <= acc) return i;
			}
			return last;
		}
	}
}
