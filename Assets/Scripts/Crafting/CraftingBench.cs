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

			var player = playerObj.GetComponent<Player>();
			if (player == null) return;

			// Validate: scraps affordable, every ingredient available, output fits.
			if (recipe.ScrapCost > 0 && player.Scraps < recipe.ScrapCost) return;

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

			// Commit: spend scraps, consume ingredients, then grant output.
			if (!player.TrySpendScraps(recipe.ScrapCost)) return;

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

		// --- Scavenging ---

		/// <summary>
		/// Destroy one unit from the player's inventory <paramref name="slotIndex"/> and grant the
		/// item's <see cref="ItemDefinition.ScrapValue"/> in scraps. Authoritative + host-range gated,
		/// mirroring <see cref="RPC_RequestCraft"/>. Scavenging is only possible at a bench.
		/// </summary>
		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_RequestScavenge(int slotIndex, RpcInfo info = default)
		{
			var source = ResolveSource(info);
			if (Runner == null) return;

			var playerObj = Runner.GetPlayerObject(source);
			if (playerObj == null) return;

			if (!IsWithinHostRange(playerObj.transform.position)) return;

			var inventory = playerObj.GetComponent<Inventory>();
			if (inventory == null) return;
			if (slotIndex < 0 || slotIndex >= Inventory.SlotCount) return;

			var slot = inventory.Slots[slotIndex];
			if (slot.IsEmpty) return;

			if (ItemDatabase.Instance == null) return;
			var def = ItemDatabase.Instance.GetById(slot.ItemId);
			if (def == null) return;

			var player = playerObj.GetComponent<Player>();
			if (player == null) return;

			// Remove one unit first; only grant scraps if the destroy actually happened.
			if (!inventory.RemoveAt(slotIndex, 1)) return;
			player.AddScraps(def.ScrapValue);
		}

		private PlayerRef ResolveSource(RpcInfo info)
		{
			return info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
		}
	}
}
