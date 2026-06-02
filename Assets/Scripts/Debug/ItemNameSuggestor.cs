using System.Collections.Generic;
using System.Linq;
using QFSW.QC;
using Starter.Common.Inventory;

namespace Starter.Shooter
{
	/// <summary>
	/// Suggestor tag marking a string command parameter as "an item name drawn from the live
	/// <see cref="ItemDatabase"/>". Applied with <see cref="ItemNameAttribute"/> and consumed by
	/// <see cref="ItemNameSuggestor"/> to drive Quantum Console autocomplete.
	/// </summary>
	public struct ItemNameTag : IQcSuggestorTag { }

	/// <summary>
	/// Marks a Quantum Console command parameter so it auto-completes from every item's DisplayName in
	/// the bound <see cref="ItemDatabase"/>. Usage: <c>void Cmd([ItemName] string item)</c>.
	/// </summary>
	public sealed class ItemNameAttribute : SuggestorTagAttribute
	{
		private readonly ItemNameTag _tag;
		public override IQcSuggestorTag[] GetSuggestorTags() => new IQcSuggestorTag[] { _tag };
	}

	/// <summary>
	/// Feeds Quantum Console autocomplete with the DisplayName of every item in the bound
	/// <see cref="ItemDatabase"/>. Auto-discovered by QC's injection loader (it implements
	/// <see cref="IQcSuggestor"/> via <see cref="BasicCachedQcSuggestor{T}"/>). Names containing spaces are
	/// suggested as single quoted literals so they parse back into one string argument. Suggestions are
	/// only available once the database is bound at runtime (GameManager.Awake) — empty otherwise.
	/// </summary>
	public sealed class ItemNameSuggestor : BasicCachedQcSuggestor<string>
	{
		protected override bool CanProvideSuggestions(SuggestionContext context, SuggestorOptions options)
		{
			return context.HasTag<ItemNameTag>();
		}

		protected override IQcSuggestion ItemToSuggestion(string itemName)
		{
			return new RawSuggestion(itemName, true);
		}

		protected override IEnumerable<string> GetItems(SuggestionContext context, SuggestorOptions options)
		{
			var db = ItemDatabase.Instance;
			if (db == null) return Enumerable.Empty<string>();

			return db.All
				.Where(def => def != null && !string.IsNullOrEmpty(def.DisplayName))
				.Select(def => def.DisplayName)
				.Distinct();
		}
	}
}
