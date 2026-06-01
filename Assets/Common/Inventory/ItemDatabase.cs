using System.Collections.Generic;
using UnityEngine;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// Asset-backed registry of every ItemDefinition the game knows about. A bootstrap
	/// MonoBehaviour (GameManager) holds the reference and calls Bind() in Awake so
	/// runtime lookups go through ItemDatabase.Instance without using Resources.
	/// </summary>
	[CreateAssetMenu(fileName = "ItemDatabase", menuName = "Inventory/Item Database", order = 1)]
	public sealed class ItemDatabase : ScriptableObject
	{
		public static ItemDatabase Instance { get; private set; }

		[SerializeField] private List<ItemDefinition> _items = new();

		[Tooltip("Shared world-pickup prefab spawned for any item without a bespoke WorldPrefab override " +
		         "(e.g. a thrown weapon landing). Same Pickup_Generic the Inventory uses; builds its visual from ItemVisual.")]
		public GameObject GenericWorldPrefab;

		private Dictionary<short, ItemDefinition> _byId;

		public IReadOnlyList<ItemDefinition> All => _items;

		public void Bind()
		{
			BuildLookup();
			Instance = this;
		}

		public ItemDefinition GetById(short id)
		{
			if (id == 0) return null;
			if (_byId == null) BuildLookup();
			_byId.TryGetValue(id, out var def);
			return def;
		}

		private void BuildLookup()
		{
			_byId = new Dictionary<short, ItemDefinition>(_items.Count);
			for (int i = 0; i < _items.Count; i++)
			{
				var def = _items[i];
				if (def == null) continue;

				if (def.Id == 0)
				{
					Debug.LogError($"[ItemDatabase] '{def.name}' has reserved Id 0; skipped.");
					continue;
				}

				if (_byId.TryGetValue(def.Id, out var existing))
				{
					Debug.LogError($"[ItemDatabase] Duplicate Id {def.Id} on '{def.name}' (conflicts with '{existing.name}'); skipped.");
					continue;
				}

				_byId[def.Id] = def;
			}
		}
	}
}
