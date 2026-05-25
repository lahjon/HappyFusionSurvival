using UnityEngine;

namespace Starter.Shooter
{
	[System.Serializable]
	public struct ItemStat
	{
		public string Label;
		public string Value;
	}

	[CreateAssetMenu(fileName = "Item", menuName = "Shooter/Item Definition", order = 0)]
	public sealed class ItemDefinition : ScriptableObject
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

		[Min(1)]
		public short MaxStack = 1;
	}
}
