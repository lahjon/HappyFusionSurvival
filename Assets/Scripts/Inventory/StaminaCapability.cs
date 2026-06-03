using Fusion;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Consumable facet that restores stamina on use (e.g. an energy drink). Mirrors
	/// <see cref="HealingCapability"/> but targets the <see cref="Player"/>'s stamina pool.
	/// </summary>
	[System.Serializable]
	public sealed class StaminaCapability : ConsumableCapability
	{
		[Tooltip("Stamina restored when used. Capped at the player's MaxStamina.")]
		[Min(1f)] public float StaminaAmount = 10f;

		public override void Apply(GameObject target, NetworkRunner runner)
		{
			if (target == null) return;
			if (target.TryGetComponent<Player>(out var player))
			{
				player.RestoreStamina(StaminaAmount);
			}
		}
	}
}
