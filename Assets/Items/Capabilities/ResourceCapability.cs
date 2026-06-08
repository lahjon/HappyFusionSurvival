using Fusion;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Consumable facet that grants the consumer a chunk of some persistent resource (money, scraps, ...).
	/// One capability covering all "grant N of resource X" effects — pick the resource and amount on the
	/// item asset rather than adding a separate capability type per resource. Both targets already guard
	/// on HasStateAuthority (<see cref="Player.AddMoney"/>/<see cref="Player.AddScraps"/>), so Apply can
	/// call them directly on every predicting peer.
	/// </summary>
	[System.Serializable]
	public sealed class ResourceCapability : ConsumableCapability
	{
		public enum Resource { Money, Scraps }

		[Tooltip("Which persistent resource this grants.")]
		public Resource Type = Resource.Money;

		[Tooltip("Amount granted when consumed.")]
		[Min(0)] public int Amount = 1;

		public override void Apply(GameObject target, NetworkRunner runner)
		{
			if (target == null) return;
			if (target.TryGetComponent<Player>(out var player) == false) return;

			switch (Type)
			{
				case Resource.Money:  player.AddMoney(Amount);  break;
				case Resource.Scraps: player.AddScraps(Amount); break;
			}
		}
	}
}
