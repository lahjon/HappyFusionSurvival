using Fusion;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	[CreateAssetMenu(fileName = "FoodConsumable", menuName = "Inventory/Consumables/Food", order = 11)]
	public sealed class FoodConsumable : ConsumableDefinition
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
