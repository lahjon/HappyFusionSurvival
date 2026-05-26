using Fusion;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	[CreateAssetMenu(fileName = "HealingConsumable", menuName = "Inventory/Consumables/Healing", order = 10)]
	public sealed class HealingConsumable : ConsumableDefinition
	{
		[Tooltip("HP restored when used. Capped at the target's InitialHealth.")]
		[Min(1)] public int HealAmount = 1;

		public override void Apply(GameObject target, NetworkRunner runner)
		{
			if (target == null) return;
			if (target.TryGetComponent<Health>(out var health))
			{
				health.Heal(HealAmount);
			}
		}
	}
}
