using System.Collections.Generic;
using UnityEngine;

namespace Starter.Common.Quests
{
	/// <summary>
	/// Asset-backed registry of every <see cref="QuestDefinition"/> the game knows about. A bootstrap
	/// MonoBehaviour (GameManager) holds the reference and calls Bind() in Awake so runtime lookups
	/// go through <see cref="Instance"/> without using Resources. Mirrors RecipeDatabase / ItemDatabase.
	/// </summary>
	[CreateAssetMenu(fileName = "QuestDatabase", menuName = "Quests/Quest Database", order = 1)]
	public sealed class QuestDatabase : ScriptableObject
	{
		public static QuestDatabase Instance { get; private set; }

		[SerializeField] private List<QuestDefinition> _quests = new();

		private Dictionary<int, QuestDefinition> _byId;

		public IReadOnlyList<QuestDefinition> All => _quests;

		public void Bind()
		{
			BuildLookup();
			Instance = this;
		}

		public QuestDefinition GetById(int id)
		{
			if (id == 0) return null;
			if (_byId == null) BuildLookup();
			_byId.TryGetValue(id, out var def);
			return def;
		}

		private void BuildLookup()
		{
			_byId = new Dictionary<int, QuestDefinition>(_quests.Count);
			for (int i = 0; i < _quests.Count; i++)
			{
				var def = _quests[i];
				if (def == null) continue;

				if (def.Id == 0)
				{
					Debug.LogError($"[QuestDatabase] '{def.name}' has reserved Id 0; skipped.");
					continue;
				}

				if (_byId.TryGetValue(def.Id, out var existing))
				{
					Debug.LogError($"[QuestDatabase] Duplicate Id {def.Id} on '{def.name}' (conflicts with '{existing.name}'); skipped.");
					continue;
				}

				_byId[def.Id] = def;
			}
		}
	}
}
