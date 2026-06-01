using System.Collections.Generic;
using UnityEngine;

namespace Starter.Common.Inventory
{
	[System.Serializable]
	public struct ItemStat
	{
		public string Label;
		public string Value;
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

		[Header("Visuals")]
		[Tooltip("Mesh/material/scale the shared generic prefabs use to build this item's world pickup and " +
		         "in-hand model. A new ordinary item needs only this — no bespoke prefab.")]
		public ItemVisual Visual = new();

		[Tooltip("Optional override: a fully bespoke world prefab (NetworkObject + PickupableItem). When set, " +
		         "it is spawned instead of the generic pickup. Leave null for ordinary items (uses Visual).")]
		public GameObject WorldPrefab;

		[Tooltip("Optional override: a fully bespoke in-hand prefab. When set, it is used instead of the generic " +
		         "hand rig (e.g. the Scanner radar device). Leave null for ordinary items (uses Visual).")]
		public GameObject HandPrefab;

		[Tooltip("Maximum items per inventory slot. 1 = not stackable (each pickup creates a new slot). >1 = stackable up to this count; overflow spills into the next slot.")]
		[Min(1)]
		public int MaxStack = 1;

		[Tooltip("Weight per unit. Total inventory weight reduces movement speed above the player's WeightLimit.")]
		[Min(0f)]
		public float Weight = 0f;

		[Tooltip("Scraps granted when ONE unit of this item is scavenged at a crafting bench. The item is destroyed in exchange for this much of the generic 'scraps' crafting currency.")]
		[Min(0)]
		public int ScrapValue = 1;

		[Header("Capabilities")]
		[Tooltip("Composable behavior modules. Add a WeaponCapability to make this item a weapon, a " +
		         "PlaceableCapability to make it placeable, a ConsumableCapability to make it usable, etc. " +
		         "Facets compose freely on one item.")]
		[SerializeReference]
		public List<ItemCapability> Capabilities = new();

		/// <summary>First capability of type <typeparamref name="T"/> on this item, or null.</summary>
		public T GetCapability<T>() where T : ItemCapability
		{
			if (Capabilities == null) return null;
			for (int i = 0; i < Capabilities.Count; i++)
			{
				if (Capabilities[i] is T match) return match;
			}
			return null;
		}

		public bool TryGetCapability<T>(out T capability) where T : ItemCapability
		{
			capability = GetCapability<T>();
			return capability != null;
		}

		public bool HasCapability<T>() where T : ItemCapability => GetCapability<T>() != null;
	}
}
