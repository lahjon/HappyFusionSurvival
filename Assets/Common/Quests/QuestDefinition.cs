using Starter.Common.Crafting;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Common.Quests
{
	/// <summary>
	/// One delivery offer — a list of items the giver wants and the items + money paid out
	/// on completion. Authored as a ScriptableObject and registered in <see cref="QuestDatabase"/>
	/// so RPCs can refer to it by stable <see cref="Id"/> without sending the asset reference.
	/// </summary>
	[CreateAssetMenu(fileName = "Quest", menuName = "Quests/Quest Definition", order = 0)]
	public class QuestDefinition : ScriptableObject
	{
		[Tooltip("Stable network id. Must be non-zero and unique within the QuestDatabase.")]
		public int Id = 1;

		public string DisplayName = "Delivery";
		public Sprite Icon;

		[TextArea(2, 6)]
		public string Description;

		[Tooltip("Items the player must hand in. Empty entries are ignored. Reuses the crafting Ingredient struct (Item + Count).")]
		public Ingredient[] Requested;

		[Tooltip("Items granted on successful turn-in. Empty entries are ignored.")]
		public Ingredient[] ItemRewards;

		[Tooltip("Money granted on successful turn-in. 0 means no money reward.")]
		[Min(0)] public int MoneyReward;
	}
}
