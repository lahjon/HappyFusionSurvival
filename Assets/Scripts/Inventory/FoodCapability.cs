using Fusion;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Consumable facet that restores hunger on use. Capability replacement for the old
	/// <c>FoodConsumable</c> ScriptableObject. (Hunger is legacy — see CLAUDE.md — but kept
	/// functional so the Food item migrates cleanly.)
	/// </summary>
	[System.Serializable]
	public sealed class FoodCapability : ConsumableCapability
	{
		[Tooltip("Hunger (fullness) restored when consumed. Result is clamped to Player.MaxHunger.")]
		[Min(0f)] public float HungerAmount = 25f;

		public override void Apply(GameObject target, NetworkRunner runner)
		{
			if (target == null) return;
			if (target.TryGetComponent<Player>(out var player))
			{
				player.AddHunger(HungerAmount);
			}
		}
	}
}
