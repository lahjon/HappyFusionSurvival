using Fusion;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Common.Crafting
{
	/// <summary>
	/// Consumable "recipe scroll": when used, grants <see cref="Recipe"/> to the target
	/// player's <see cref="IRecipeBook"/>. Lives in Common because the data is shared;
	/// per-mode players supply the concrete RecipeBook implementation.
	/// </summary>
	[CreateAssetMenu(fileName = "RecipeScroll", menuName = "Crafting/Recipe Unlock Consumable", order = 10)]
	public sealed class RecipeUnlockConsumable : ConsumableDefinition
	{
		[Tooltip("The recipe granted when this scroll is used.")]
		public RecipeDefinition Recipe;

		public override void Apply(GameObject target, NetworkRunner runner)
		{
			if (target == null || Recipe == null || Recipe.Id == 0) return;

			var book = target.GetComponent<IRecipeBook>();
			if (book == null) return;

			book.Unlock(Recipe.Id);
		}
	}
}
