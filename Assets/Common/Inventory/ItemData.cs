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

	[System.Serializable]
	public struct ItemWorldPhysics
	{
		[Tooltip("ON: when dropped in a WaterVolume, this item floats to the surface and self-rights " +
		         "(e.g. a sealed case, a cork). OFF (default): it sinks. Read by the generic pickup; bespoke " +
		         "WorldPrefab items use this too via PickupableItem.")]
		public bool Floats;

		[Tooltip("ON: override the dropped item's rigidbody center of mass with the offset below. A low COM " +
		         "(negative Y) makes the item bottom-heavy so it self-rights and floats the right way up. " +
		         "Only relevant when Floats is on — a sinking item keeps Unity's auto-computed COM.")]
		public bool OverrideCenterOfMass;

		[Tooltip("Local-space center of mass when the override is on.")]
		public Vector3 CenterOfMass;
	}

	[CreateAssetMenu(fileName = "Item", menuName = "Inventory/Item Data", order = 0)]
	public class ItemData : ScriptableObject
	{
		[Tooltip("Stable network id. Must be non-zero and unique within the ItemDatabase.")]
		public short Id = 1;

		public string DisplayName = "Item";
		public Sprite Icon;

		[TextArea(2, 6)]
		public string Description;

		[Tooltip("Optional list of stat rows shown in the tooltip (e.g. Damage / 12).")]
		public ItemStat[] Stats;

		[Header("Setup")]
		[Tooltip("Visual-only prefab instantiated as this item's model on the shared world pickup and hand " +
		         "rig (Pickup_Generic / Hand_Generic). The single source of an ordinary item's look — no " +
		         "gameplay components. Leave null to fall back to the generic rig's placeholder primitive.")]
		public GameObject Prefab;

		[Tooltip("ON: picking this item up runs every ConsumableCapability's effect immediately and skips " +
		         "the inventory entirely (the pickup is consumed/despawned on the spot).")]
		public bool ConsumeOnPickup = false;

		[Tooltip("Optional sound played locally on the player when this item is picked up.")]
		public AudioClip PickupAudio;

		[Tooltip("Optional sound played locally on the player when this item is used from the inventory " +
		         "(e.g. drinking a potion, eating food for its healing effect).")]
		public AudioClip OnUseAudio;

		[Tooltip("Maximum items per inventory slot. 1 = not stackable (each pickup creates a new slot). >1 = stackable up to this count; overflow spills into the next slot.")]
		[Min(1)]
		public int MaxStack = 1;

		[Tooltip("Weight per unit. Total inventory weight reduces movement speed above the player's WeightLimit.")]
		[Min(0f)]
		public float Weight = 0f;

		[Tooltip("Scraps granted when ONE unit of this item is scavenged at a crafting bench. The item is destroyed in exchange for this much of the generic 'scraps' crafting currency.")]
		[Min(0)]
		public int ScrapValue = 1;

		[Tooltip("Base economic value (money). A procedurally-stocked shop sets its buy price to BaseValue " +
		         "scaled by the shop's markup; the sell price is a percentage of that buy price.")]
		[Min(0)]
		public int BaseValue = 10;

		[Tooltip("Scale/offset/flair the shared generic prefabs use to position this item's model " +
		         "(see Prefab above) on the world pickup and in-hand rig, plus optional bespoke " +
		         "WorldPrefab/HandPrefab overrides.")]
		public ItemVisual Visual = new();

		[Tooltip("Floating / center-of-mass tuning for this item's dropped-in-world rigidbody. Collapsed " +
		         "by default — expand only when this item needs custom water/buoyancy behaviour.")]
		public ItemWorldPhysics WorldPhysics;

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

		/// <summary>
		/// The <c>InventorySlot.Loaded</c> value a fresh unit of this item should enter a slot with — the max
		/// <see cref="ItemCapability.InitialLoaded"/> across its capabilities (e.g. a gadget's charge count).
		/// 0 for ordinary items. Used by <c>InventoryOps.TryAdd</c> when filling an empty slot.
		/// </summary>
		public short InitialLoaded()
		{
			if (Capabilities == null) return 0;
			short max = 0;
			for (int i = 0; i < Capabilities.Count; i++)
			{
				if (Capabilities[i] == null) continue;
				short v = Capabilities[i].InitialLoaded;
				if (v > max) max = v;
			}
			return max;
		}
	}
}
