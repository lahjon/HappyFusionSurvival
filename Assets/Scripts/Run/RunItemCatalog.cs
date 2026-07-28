using System.Collections.Generic;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Run
{
	/// <summary>One logical tool bound to the real inventory item that represents it.</summary>
	[System.Serializable]
	public struct RunItemBinding
	{
		[Tooltip("Logical requirement the randomizer reasons about.")]
		public RunItemKind Kind;

		[Tooltip("Inventory item that satisfies it. Leave null for knowledge-only kinds (pin codes, intel).")]
		public ItemData Item;
	}

	/// <summary>
	/// Bridge between the randomizer's logical items and the game's real inventory items.
	///
	/// <para>The solver deals in <see cref="RunItemKind.Crowbar"/>; the player carries a crowbar
	/// <see cref="ItemData"/> with a network id and a weight and a model. Keeping the two apart means the logic
	/// layer never has to know about the inventory, and the inventory never has to know it is part of a puzzle —
	/// so item art, stats and ids can change without touching a line of solver code.</para>
	///
	/// <para>Knowledge kinds (<see cref="RunItemKind.PinCode"/>, <see cref="RunItemKind.Intel"/>,
	/// <see cref="RunItemKind.NavUnlock"/>, <see cref="RunItemKind.Power"/>) deliberately have no binding: they
	/// are not carried, they are recorded on <see cref="RunState"/> and shared by the whole team.</para>
	/// </summary>
	[CreateAssetMenu(menuName = "HorrorTown/Run Item Catalog", fileName = "RunItemCatalog")]
	public sealed class RunItemCatalog : ScriptableObject
	{
		[Tooltip("Logical tool → inventory item. Keycards and keys use the generic entries below; variants are " +
			"distinguished by the run's own bookkeeping, not by separate item assets.")]
		public List<RunItemBinding> Bindings = new List<RunItemBinding>();

		[Header("Keys and cards")]
		[Tooltip("Key ring item. One asset covers every mechanical lock in a run — the planner never varies keys.")]
		public ItemData KeyItem;

		[Tooltip("One item per keycard tier, in order (tier 1 at index 0). Keycards are the one physical item the " +
			"randomizer varies, so each tier needs its own asset for the inventory to tell them apart.")]
		public ItemData[] KeycardTiers;

		private Dictionary<RunItemKind, ItemData> _byKind;

		/// <summary>Inventory item for a specific requirement, honouring keycard tiers.</summary>
		public ItemData Resolve(RunItem item)
		{
			if (item.Kind == RunItemKind.Keycard && KeycardTiers != null && KeycardTiers.Length > 0)
			{
				int tier = Mathf.Clamp(item.Variant - 1, 0, KeycardTiers.Length - 1);
				return KeycardTiers[tier];
			}
			return Resolve(item.Kind);
		}

		public short ResolveId(RunItem item)
		{
			var data = Resolve(item);
			return data != null ? data.Id : (short)0;
		}

		/// <summary>Build the lookup. Call once on load (the director does it) — mirrors <c>ItemDatabase.Bind</c>.</summary>
		public void Bind()
		{
			_byKind = new Dictionary<RunItemKind, ItemData>(Bindings.Count + 2);
			for (int i = 0; i < Bindings.Count; i++)
			{
				var binding = Bindings[i];
				if (binding.Item == null) continue;
				_byKind[binding.Kind] = binding.Item;
			}
			if (KeyItem != null) _byKind[RunItemKind.Key] = KeyItem;
			if (KeycardTiers != null && KeycardTiers.Length > 0 && KeycardTiers[0] != null)
				_byKind[RunItemKind.Keycard] = KeycardTiers[0];
		}

		/// <summary>Inventory item that satisfies <paramref name="kind"/>, or null when it is knowledge or unbound.</summary>
		public ItemData Resolve(RunItemKind kind)
		{
			if (_byKind == null) Bind();
			return _byKind.TryGetValue(kind, out var item) ? item : null;
		}

		public short ResolveId(RunItemKind kind)
		{
			var item = Resolve(kind);
			return item != null ? item.Id : (short)0;
		}
	}
}
