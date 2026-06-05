using Fusion;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Hunger
{
	/// <summary>
	/// Consumable facet that restores hunger (fullness) on use. Targets the <see cref="HungerSystem"/> on the consumer;
	/// if hunger is currently disabled (no HungerSystem on the player) the effect simply no-ops. Part of the dormant
	/// Starter.Hunger module — see <see cref="HungerSystem"/>. Composes onto any <c>ItemData</c> like the other
	/// <see cref="ConsumableCapability"/> facets (healing, recipe scrolls).
	/// </summary>
	[System.Serializable]
	public sealed class FoodCapability : ConsumableCapability
	{
		[Tooltip("Hunger (fullness) restored when consumed. Result is clamped to HungerSystem.MaxHunger.")]
		[Min(0f)] public float HungerAmount = 25f;

		public override void Apply(GameObject target, NetworkRunner runner)
		{
			if (target == null) return;
			if (target.TryGetComponent<HungerSystem>(out var hunger))
				hunger.AddHunger(HungerAmount);
		}
	}
}
