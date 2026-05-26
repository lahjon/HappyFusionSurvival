using System.Collections.Generic;
using UnityEngine;

namespace Starter.Common.Crafting
{
	/// <summary>
	/// Asset-backed registry of every RecipeDefinition the game knows about. A bootstrap
	/// MonoBehaviour (GameManager) holds the reference and calls Bind() in Awake so
	/// runtime lookups go through RecipeDatabase.Instance without using Resources.
	/// </summary>
	[CreateAssetMenu(fileName = "RecipeDatabase", menuName = "Crafting/Recipe Database", order = 1)]
	public sealed class RecipeDatabase : ScriptableObject
	{
		public static RecipeDatabase Instance { get; private set; }

		[SerializeField] private List<RecipeDefinition> _recipes = new();

		private Dictionary<int, RecipeDefinition> _byId;

		public IReadOnlyList<RecipeDefinition> All => _recipes;

		public void Bind()
		{
			BuildLookup();
			Instance = this;
		}

		public RecipeDefinition GetById(int id)
		{
			if (id == 0) return null;
			if (_byId == null) BuildLookup();
			_byId.TryGetValue(id, out var def);
			return def;
		}

		private void BuildLookup()
		{
			_byId = new Dictionary<int, RecipeDefinition>(_recipes.Count);
			for (int i = 0; i < _recipes.Count; i++)
			{
				var def = _recipes[i];
				if (def == null) continue;

				if (def.Id == 0)
				{
					Debug.LogError($"[RecipeDatabase] '{def.name}' has reserved Id 0; skipped.");
					continue;
				}

				if (_byId.TryGetValue(def.Id, out var existing))
				{
					Debug.LogError($"[RecipeDatabase] Duplicate Id {def.Id} on '{def.name}' (conflicts with '{existing.name}'); skipped.");
					continue;
				}

				_byId[def.Id] = def;
			}
		}
	}
}
