using Fusion;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Common.Crafting
{
	/// <summary>
	/// Consumable facet "recipe scroll": when used, grants <see cref="Recipe"/> to the target
	/// player's <see cref="IRecipeBook"/>. Capability replacement for the old
	/// <c>RecipeUnlockConsumable</c> ScriptableObject. Lives in Common because the data is shared;
	/// per-mode players supply the concrete RecipeBook implementation.
	/// </summary>
	[System.Serializable]
	public sealed class RecipeUnlockCapability : ConsumableCapability
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
