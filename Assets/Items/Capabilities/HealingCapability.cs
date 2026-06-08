using Fusion;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Consumable facet that restores health on use. Capability replacement for the old
	/// <c>HealingConsumable</c> ScriptableObject.
	/// </summary>
	[System.Serializable]
	public sealed class HealingCapability : ConsumableCapability
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
