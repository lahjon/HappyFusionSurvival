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

		[SerializeField] private List<ItemData> _items = new();

		[Tooltip("Shared world-pickup prefab spawned for any item without a bespoke WorldPrefab override " +
		         "(e.g. a thrown weapon landing). Same Pickup_Generic the Inventory uses; builds its visual from ItemVisual.")]
		public GameObject GenericWorldPrefab;

		private Dictionary<short, ItemData> _byId;

		public IReadOnlyList<ItemData> All => _items;

		public void Bind()
		{
			BuildLookup();
			Instance = this;
		}

		public ItemData GetById(short id)
		{
			if (id == 0) return null;
			if (_byId == null) BuildLookup();
			_byId.TryGetValue(id, out var def);
			return def;
		}

		/// <summary>
		/// Centralised null-safe lookup. Resolves <paramref name="id"/> against the bound database and
		/// returns true only when a non-null definition is found; returns false (with <paramref name="def"/>
		/// null) when no database is bound or the id is unknown. Lets call sites drop the repetitive
		/// <c>Instance == null ? ... : Instance.GetById(...)</c> guard scattered across inventory/UI code.
		/// </summary>
		public static bool TryGet(short id, out ItemData def)
		{
			def = Instance != null ? Instance.GetById(id) : null;
			return def != null;
		}

		private void BuildLookup()
		{
			_byId = new Dictionary<short, ItemData>(_items.Count);
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
