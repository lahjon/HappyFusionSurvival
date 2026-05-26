using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Common.Crafting
{
	[CreateAssetMenu(fileName = "Recipe", menuName = "Crafting/Recipe Definition", order = 0)]
	public class RecipeDefinition : ScriptableObject
	{
		[Tooltip("Stable network id. Must be non-zero and unique within the RecipeDatabase.")]
		public int Id = 1;

		public string DisplayName = "Recipe";
		public Sprite Icon;

		[TextArea(2, 6)]
		public string Description;

		[Tooltip("Items consumed when this recipe is crafted. Empty entries are ignored.")]
		public Ingredient[] Ingredients;

		[Tooltip("Item granted on successful craft.")]
		public ItemDefinition Output;

		[Min(1)]
		[Tooltip("Count of Output produced per craft.")]
		public short OutputCount = 1;

		[Tooltip("If true, this recipe starts in every player's RecipeBook on spawn.")]
		public bool UnlockedByDefault;
	}
}
