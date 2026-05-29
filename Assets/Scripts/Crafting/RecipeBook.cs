using System;
using Fusion;
using Starter.Common.Crafting;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Per-player networked set of recipe ids the player has unlocked.
	/// Sibling of Inventory on the player's NetworkObject. State authority owns all writes;
	/// recipes flagged <c>UnlockedByDefault</c> are seeded in Spawned(), additional unlocks
	/// come from Unlock() called by Recipe Scrolls and bench-discover RPCs.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	public sealed class RecipeBook : NetworkBehaviour, IRecipeBook
	{
		public const int KnownCapacity = 256;

		[Networked, Capacity(KnownCapacity), OnChangedRender(nameof(OnKnownChangedRender))]
		public NetworkDictionary<int, NetworkBool> Known => default;

		public event Action KnownChanged;

		public override void Spawned()
		{
			if (HasStateAuthority == false) return;
			if (RecipeDatabase.Instance == null) return;

			var all = RecipeDatabase.Instance.All;
			for (int i = 0; i < all.Count; i++)
			{
				var def = all[i];
				if (def == null || def.Id == 0) continue;
				if (def.UnlockedByDefault == false) continue;
				Known.Set(def.Id, true);
			}
		}

		public bool IsKnown(int recipeId)
		{
			if (recipeId == 0) return false;
			return Known.TryGet(recipeId, out var known) && known;
		}

		/// <summary>State authority only. Idempotent — safe to call for an already-known recipe.</summary>
		public void Unlock(int recipeId)
		{
			if (HasStateAuthority == false) return;
			if (recipeId == 0) return;
			Known.Set(recipeId, true);
		}

		private void OnKnownChangedRender()
		{
			KnownChanged?.Invoke();
		}
	}
}
