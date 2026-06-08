using Fusion;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Consumable facet that grants the consumer money. <see cref="Player.AddMoney"/> already guards on
	/// HasStateAuthority, so Apply can call it directly on every predicting peer.
	/// </summary>
	[System.Serializable]
	public sealed class MoneyCapability : ConsumableCapability
	{
		[Tooltip("Money granted when consumed.")]
		[Min(0)] public int Amount = 1;

		public override void Apply(GameObject target, NetworkRunner runner)
		{
			if (target == null) return;
			if (target.TryGetComponent<Player>(out var player))
				player.AddMoney(Amount);
		}
	}
}
