using System;
using Fusion;
using UnityEngine;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// Item facet for things that are spent when used. <c>Inventory.TryUseAt</c> looks up the
	/// first ConsumableCapability on the selected item, enforces <see cref="UseCooldownSeconds"/>
	/// (shared across the whole inventory), calls <see cref="Apply"/>, then decrements the stack.
	///
	/// Apply runs on every predicting peer during FixedUpdateNetwork; any networked mutation inside
	/// the effect must guard on HasStateAuthority itself. Mirrors the old <c>ConsumableDefinition</c>
	/// SO contract, relocated from an ItemData subclass to a composable capability.
	/// </summary>
	[Serializable]
	public abstract class ConsumableCapability : ItemCapability
	{
		[Tooltip("Seconds before the next use of any consumable is allowed. Shared across the inventory.")]
		[Min(0f)] public float UseCooldownSeconds = 1f;

		public abstract void Apply(GameObject target, NetworkRunner runner);
	}
}
