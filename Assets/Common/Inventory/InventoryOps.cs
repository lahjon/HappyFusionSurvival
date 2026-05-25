using Fusion;
using UnityEngine;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// Pure helpers that mutate one or two <see cref="NetworkArray{InventorySlot}"/>s.
	/// Must run on state authority — callers (Inventory, LootContainer) handle authority gating.
	/// </summary>
	public static class InventoryOps
	{
		/// <summary>
		/// Add as many of <paramref name="itemId"/> as will fit into <paramref name="arr"/>:
		/// first by topping up existing matching stacks (respecting MaxStack), then by filling
		/// empty slots. Returns the leftover that did not fit.
		/// </summary>
		public static short TryAdd(NetworkArray<InventorySlot> arr, short itemId, short count)
		{
			if (itemId == 0 || count <= 0) return count;
			if (ItemDatabase.Instance == null) return count;

			var def = ItemDatabase.Instance.GetById(itemId);
			if (def == null) return count;

			short remaining = count;

			for (int i = 0; i < arr.Length && remaining > 0; i++)
			{
				var s = arr[i];
				if (s.ItemId != itemId) continue;

				short room = (short)(def.MaxStack - s.Count);
				if (room <= 0) continue;

				short add = (short)Mathf.Min(room, remaining);
				s.Count += add;
				arr.Set(i, s);
				remaining -= add;
			}

			for (int i = 0; i < arr.Length && remaining > 0; i++)
			{
				var s = arr[i];
				if (!s.IsEmpty) continue;

				short add = (short)Mathf.Min(def.MaxStack, remaining);
				arr.Set(i, new InventorySlot { ItemId = itemId, Count = add });
				remaining -= add;
			}

			return remaining;
		}

		/// <summary>
		/// Drag-drop semantics: move/merge/swap the stack at <c>from[fromIdx]</c> into <c>to[toIdx]</c>.
		/// Empty target → straight move. Same item → merge respecting MaxStack, leftover stays at source.
		/// Different item → swap. Caller must early-out on same-slot moves within one array.
		/// </summary>
		public static bool TryMove(NetworkArray<InventorySlot> from, int fromIdx,
		                           NetworkArray<InventorySlot> to,   int toIdx)
		{
			if (fromIdx < 0 || fromIdx >= from.Length) return false;
			if (toIdx   < 0 || toIdx   >= to.Length)   return false;

			var src = from[fromIdx];
			if (src.IsEmpty) return false;

			var dst = to[toIdx];

			if (dst.IsEmpty)
			{
				to.Set(toIdx, src);
				from.Set(fromIdx, InventorySlot.Empty);
				return true;
			}

			if (dst.ItemId == src.ItemId && ItemDatabase.Instance != null)
			{
				var def = ItemDatabase.Instance.GetById(src.ItemId);
				if (def != null)
				{
					short room = (short)(def.MaxStack - dst.Count);
					if (room > 0)
					{
						short move = (short)Mathf.Min(room, src.Count);
						dst.Count += move;
						src.Count -= move;
						to.Set(toIdx, dst);
						from.Set(fromIdx, src.Count <= 0 ? InventorySlot.Empty : src);
						return true;
					}
					return false;
				}
			}

			to.Set(toIdx, src);
			from.Set(fromIdx, dst);
			return true;
		}

		/// <summary>
		/// Right-click / Take-All semantics: try to push the whole stack at <c>from[fromIdx]</c>
		/// into <paramref name="to"/> via <see cref="TryAdd"/>. Leftover (e.g. <paramref name="to"/>
		/// full) is written back to <c>from[fromIdx]</c>. Returns true if anything actually moved.
		/// </summary>
		public static bool TryAutoMergeOrPlace(NetworkArray<InventorySlot> from, int fromIdx,
		                                       NetworkArray<InventorySlot> to)
		{
			if (fromIdx < 0 || fromIdx >= from.Length) return false;

			var src = from[fromIdx];
			if (src.IsEmpty) return false;

			short leftover = TryAdd(to, src.ItemId, src.Count);
			if (leftover == src.Count) return false;

			if (leftover <= 0)
			{
				from.Set(fromIdx, InventorySlot.Empty);
			}
			else
			{
				src.Count = leftover;
				from.Set(fromIdx, src);
			}
			return true;
		}
	}
}
