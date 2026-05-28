using Fusion;
using Starter.Common.Crafting;
using Starter.Common.Interactions;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Networked crafting station. Inherits <see cref="InteractableStation"/> for the
	/// shared IInteractable boilerplate; on interact, hands off to the local
	/// <see cref="CraftingSession"/> which drives the menu UI.
	///
	/// Multi-user: nothing on the bench is mutated when a player crafts, so there is no
	/// exclusivity lock (unlike <see cref="Starter.Common.Inventory.LootContainer"/>).
	/// </summary>
	public sealed class CraftingBench : InteractableStation
	{
		[Header("Crafting")]
		[Tooltip("Recipes this bench can craft. Each bench is a specific station (workbench, forge, ...) with a disjoint set.")]
		public RecipeDefinition[] AllowedRecipes;

		public bool IsRecipeAllowed(int recipeId)
		{
			if (recipeId == 0 || AllowedRecipes == null) return false;
			for (int i = 0; i < AllowedRecipes.Length; i++)
			{
				var r = AllowedRecipes[i];
				if (r != null && r.Id == recipeId) return true;
			}
			return false;
		}

		protected override void OnInteract(InteractionScanner scanner)
		{
			var session = scanner.GetComponent<CraftingSession>();
			if (session != null) session.TryOpen(this);
		}

		// --- Crafting ---

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_RequestCraft(int recipeId, RpcInfo info = default)
		{
			var source = ResolveSource(info);
			if (Runner == null) return;

			var playerObj = Runner.GetPlayerObject(source);
			if (playerObj == null) return;

			if (!IsWithinHostRange(playerObj.transform.position)) return;

			if (!IsRecipeAllowed(recipeId)) return;

			if (RecipeDatabase.Instance == null) return;
			var recipe = RecipeDatabase.Instance.GetById(recipeId);
			if (recipe == null || recipe.Output == null || recipe.OutputCount <= 0) return;

			var book = playerObj.GetComponent<RecipeBook>();
			if (book == null || !book.IsKnown(recipeId)) return;

			var inventory = playerObj.GetComponent<Inventory>();
			if (inventory == null) return;

			// Validate: every ingredient available, output fits.
			if (recipe.Ingredients != null)
			{
				for (int i = 0; i < recipe.Ingredients.Length; i++)
				{
					var ing = recipe.Ingredients[i];
					if (ing.Item == null || ing.Count <= 0) continue;
					if (InventoryOps.CountItem(inventory.Slots, ing.Item.Id) < ing.Count) return;
				}
			}

			if (InventoryOps.RoomFor(inventory.Slots, recipe.Output.Id) < recipe.OutputCount) return;

			// Commit: consume ingredients, then grant output.
			if (recipe.Ingredients != null)
			{
				for (int i = 0; i < recipe.Ingredients.Length; i++)
				{
					var ing = recipe.Ingredients[i];
					if (ing.Item == null || ing.Count <= 0) continue;
					InventoryOps.TryRemoveItem(inventory.Slots, ing.Item.Id, ing.Count);
				}
			}

			inventory.TryAdd(recipe.Output.Id, recipe.OutputCount);
		}

		private PlayerRef ResolveSource(RpcInfo info)
		{
			return info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
		}
	}
}
