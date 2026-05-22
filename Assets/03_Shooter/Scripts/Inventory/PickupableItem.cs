using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Networked world object representing a single dropped/lootable stack.
	/// Pickup is orchestrated by Inventory.RPC_RequestPickup — this component
	/// only carries the networked item identity and stack count.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	public sealed class PickupableItem : NetworkBehaviour
	{
		[Header("Authoring (scene-placed pickups)")]
		[Tooltip("Used when a pickup is placed in the scene. Programmatic spawns call Initialize() instead.")]
		[SerializeField] private ItemDefinition _initialItem;
		[SerializeField, Min(1)] private short _initialCount = 1;

		[Networked] public short ItemId { get; set; }
		[Networked] public short Count { get; set; }

		public ItemDefinition Definition =>
			ItemDatabase.Instance != null ? ItemDatabase.Instance.GetById(ItemId) : null;

		/// <summary>Authority-only. Call right after Runner.Spawn for programmatic pickups.</summary>
		public void Initialize(short itemId, short count)
		{
			ItemId = itemId;
			Count = count;
		}

		public override void Spawned()
		{
			if (Object.HasStateAuthority && ItemId == 0 && _initialItem != null)
			{
				ItemId = _initialItem.Id;
				Count = _initialCount;
			}
		}
	}
}
