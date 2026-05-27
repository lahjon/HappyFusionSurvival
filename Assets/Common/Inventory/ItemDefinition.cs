using Fusion;
using UnityEngine;

namespace Starter.Common.Inventory
{
	public enum EItemSize : byte
	{
		Small = 0,
		Large = 1,
	}

	[System.Serializable]
	public struct ItemStat
	{
		public string Label;
		public string Value;
	}

	// Subclass of ItemDefinition for items that are spent when used.
	// Apply runs on every predicting peer during FixedUpdateNetwork; any networked
	// mutations inside the effect must guard on HasStateAuthority themselves.
	public abstract class ConsumableDefinition : ItemDefinition
	{
		[Tooltip("Seconds before the next use of any consumable is allowed. Shared across the inventory.")]
		[Min(0f)] public float UseCooldownSeconds = 1f;

		public abstract void Apply(GameObject target, NetworkRunner runner);
	}

	[CreateAssetMenu(fileName = "Item", menuName = "Inventory/Item Definition", order = 0)]
	public class ItemDefinition : ScriptableObject
	{
		[Tooltip("Stable network id. Must be non-zero and unique within the ItemDatabase.")]
		public short Id = 1;

		public string DisplayName = "Item";
		public Sprite Icon;

		[TextArea(2, 6)]
		public string Description;

		[Tooltip("Optional list of stat rows shown in the tooltip (e.g. Damage / 12).")]
		public ItemStat[] Stats;

		[Tooltip("World prefab spawned when this item is dropped or seeded as loot. Must have NetworkObject + PickupableItem.")]
		public GameObject WorldPrefab;

		[Tooltip("Local-only model parented to the player's HandAnchor when this slot is selected.")]
		public GameObject HandPrefab;

		[Tooltip("Maximum items per inventory slot. 1 = not stackable (each pickup creates a new slot). >1 = stackable up to this count; overflow spills into the next slot.")]
		[Min(1)]
		public int MaxStack = 1;

		[Tooltip("Small: stacks/moves normally. Large: can only exist in the inventory while equipped (selected slot). Pickup auto-equips; selecting away or swap-moving drops it.")]
		public EItemSize Size = EItemSize.Small;

		[Tooltip("Weight per unit. Total inventory weight reduces movement speed above the player's WeightLimit.")]
		[Min(0f)]
		public float Weight = 0f;
	}
}
