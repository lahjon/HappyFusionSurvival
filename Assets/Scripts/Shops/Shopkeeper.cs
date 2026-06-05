using System.Collections.Generic;
using Fusion;
using Starter.Common;
using Starter.Common.Interactions;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// One row in a shopkeeper's stock — an item that the shop sells, its per-unit buy price,
	/// and the starting stock count seeded into <see cref="Shopkeeper.RemainingStock"/> on spawn.
	/// </summary>
	[System.Serializable]
	public struct ShopOffer
	{
		public ItemData Item;

		[Min(0)] public int BuyPrice;

		[Tooltip("Stock count seeded into RemainingStock on spawn. 0 means the offer is sold out from the start (effectively hidden).")]
		[Min(0)] public int StartingStock;
	}

	/// <summary>
	/// Networked shopkeeper. Same interaction shape as <see cref="QuestGiver"/> — extends
	/// <see cref="InteractableStation"/>, opens a per-player <see cref="ShopSession"/> on interact —
	/// but instead of one-shot quest claims, exposes 4 buy offers with limited per-offer stock and
	/// accepts the player's hotbar items for sale at <see cref="SellPriceMultiplierPercent"/>% of the
	/// matching offer's buy price. Items the shop doesn't sell aren't priced — the player can still
	/// see them in the sell column but the SELL button is disabled.
	///
	/// Phase-gated to Day (Lobby allowed for pre-match testing), matching the vendor rule in
	/// CLAUDE.md (shops self-disable during the Purge).
	/// </summary>
	public sealed class Shopkeeper : InteractableStation
	{
		/// <summary>Hard cap on offers per shopkeeper; mirrors the networked array capacity below.</summary>
		public const int MaxOffers = 4;

		[Header("Shop")]
		[Tooltip("Optional procedural-stock profile. When assigned, the shop rolls its offers from the " +
		         "profile's loot tables at spawn (state authority) instead of using the authored Offers list.")]
		[SerializeField] private ShopStockProfile _stockProfile;

		[Tooltip("Hand-authored fallback offers. Used only when no Stock Profile is assigned. Capped at MaxOffers.")]
		public ShopOffer[] Offers;

		[Tooltip("Percent of an offer's buy price that the shopkeeper pays when the player sells the matching item. 50 = half price. Items not in the offer list are not bought (0).")]
		[Range(0, 200)] public int SellPriceMultiplierPercent = 50;

		/// <summary>
		/// Remaining stock per offer slot. State authority is the only writer. Reads on every peer
		/// drive the UI ("3 left", greyed-out "SOLD OUT", etc.). Seeded in <see cref="Spawned"/>.
		/// </summary>
		[Networked, Capacity(MaxOffers), OnChangedRender(nameof(OnStockChangedRender))]
		public NetworkArray<int> RemainingStock => default;

		/// <summary>
		/// Resolved item id per offer slot (0 = empty). State authority writes this at spawn from
		/// either the rolled profile or the authored <see cref="Offers"/> list, so every peer reads
		/// the same stock through one networked source. Resolved to an ItemData via ItemDatabase.
		/// </summary>
		[Networked, Capacity(MaxOffers)]
		public NetworkArray<short> OfferItemId => default;

		/// <summary>Buy price per offer slot, paired with <see cref="OfferItemId"/>.</summary>
		[Networked, Capacity(MaxOffers)]
		public NetworkArray<int> OfferBuyPrice => default;

		/// <summary>Number of live offer slots (≤ <see cref="MaxOffers"/>). UI iterates [0, OfferCount).</summary>
		[Networked, OnChangedRender(nameof(OnStockChangedRender))]
		public int OfferCountNet { get; private set; }

		public event System.Action StockChanged;

		public override bool CanInteract => IsAvailablePhase();
		public override string LockedReason => IsAvailablePhase() ? string.Empty : $"{DisplayName} is closed";

		public override void Spawned()
		{
			base.Spawned();

			if (!HasStateAuthority) return;

			if (_stockProfile != null && _stockProfile.TableSet != null)
				GenerateStock();
			else
				SeedAuthoredStock();
		}

		/// <summary>
		/// Rolls offers from the assigned <see cref="ShopStockProfile"/> using a deterministic,
		/// position-seeded RNG (state authority only). Writes the resolved item id, buy price and
		/// starting stock into the networked arrays so clients replicate the same shelf.
		/// </summary>
		private void GenerateStock()
		{
			var rng = WorldGen.RngFor(transform.position);

			int min = Mathf.Clamp(_stockProfile.MinOffers, 0, MaxOffers);
			int max = Mathf.Clamp(_stockProfile.MaxOffers, min, MaxOffers);
			int target = max > min ? min + rng.Next(max - min + 1) : min;

			var usedIds = _stockProfile.UniqueItems ? new HashSet<short>() : null;
			int written = 0;

			// Bounded attempts so a small/duplicate-heavy table can't spin forever.
			int attempts = 0;
			int maxAttempts = target * 8 + 16;
			while (written < target && attempts < maxAttempts)
			{
				attempts++;
				if (!_stockProfile.TableSet.TryRoll(rng, out var entry) || entry.Item == null) continue;

				short id = entry.Item.Id;
				if (id == 0) continue;
				if (usedIds != null && !usedIds.Add(id)) continue;

				int stock = RollStock(rng);
				OfferItemId.Set(written, id);
				OfferBuyPrice.Set(written, _stockProfile.BuyPriceFor(entry.Item));
				RemainingStock.Set(written, stock);
				written++;
			}

			OfferCountNet = written;
		}

		private int RollStock(System.Random rng)
		{
			int lo = Mathf.Max(0, _stockProfile.StockRange.x);
			int hi = Mathf.Max(lo, _stockProfile.StockRange.y);
			return hi > lo ? lo + rng.Next(hi - lo + 1) : lo;
		}

		/// <summary>Copies the hand-authored <see cref="Offers"/> into the networked arrays (fallback path).</summary>
		private void SeedAuthoredStock()
		{
			if (Offers == null) { OfferCountNet = 0; return; }

			int count = Mathf.Min(Offers.Length, MaxOffers);
			int written = 0;
			for (int i = 0; i < count; i++)
			{
				var o = Offers[i];
				if (o.Item == null || o.Item.Id == 0) continue;
				OfferItemId.Set(written, o.Item.Id);
				OfferBuyPrice.Set(written, Mathf.Max(0, o.BuyPrice));
				RemainingStock.Set(written, Mathf.Max(0, o.StartingStock));
				written++;
			}
			OfferCountNet = written;
		}

		protected override void OnInteract(InteractionScanner scanner)
		{
			var session = scanner.GetComponent<ShopSession>();
			if (session != null) session.TryOpen(this);
		}

		/// <summary>
		/// Opens this shop for the local player. Safe to wire to a UnityEvent or
		/// trigger collider — no arguments required.
		/// </summary>
		public void OpenMenu()
		{
			var playerObj = Runner != null ? Runner.GetPlayerObject(Runner.LocalPlayer) : null;
			var session   = playerObj != null
				? playerObj.GetComponent<ShopSession>()
				: FindAnyObjectByType<ShopSession>();
			session?.TryOpen(this);
		}

		/// <summary>Number of live offer slots (capped at <see cref="MaxOffers"/>). UI iterates [0, OfferCount).</summary>
		public int OfferCount => Mathf.Clamp(OfferCountNet, 0, MaxOffers);

		/// <summary>
		/// Resolves offer <paramref name="index"/> from the networked arrays into a <see cref="ShopOffer"/>
		/// (Item via ItemDatabase, current buy price, current remaining stock). Same return shape the UI
		/// already consumes, so authored and procedural stock read identically.
		/// </summary>
		public ShopOffer GetOffer(int index)
		{
			if (index < 0 || index >= OfferCount) return default;
			short id = OfferItemId[index];
			if (id == 0) return default;

			var db = ItemDatabase.Instance;
			return new ShopOffer
			{
				Item = db != null ? db.GetById(id) : null,
				BuyPrice = OfferBuyPrice[index],
				StartingStock = RemainingStock[index],
			};
		}

		/// <summary>
		/// Sell price the shop pays for one unit of <paramref name="itemId"/>, or 0 if the shop
		/// doesn't carry that item. UI uses this both to label the SELL button and to gate it.
		/// </summary>
		public int GetSellPrice(short itemId)
		{
			if (itemId == 0) return 0;
			int count = OfferCount;
			for (int i = 0; i < count; i++)
			{
				if (OfferItemId[i] == itemId)
				{
					return Mathf.Max(0, (OfferBuyPrice[i] * SellPriceMultiplierPercent) / 100);
				}
			}
			return 0;
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_RequestBuy(int offerIndex, RpcInfo info = default)
		{
			if (Runner == null) return;
			if (offerIndex < 0 || offerIndex >= OfferCount) return;
			if (!IsAvailablePhase()) return;

			if (RemainingStock[offerIndex] <= 0) return;

			var offer = GetOffer(offerIndex);
			if (offer.Item == null) return;

			var source = ResolveSource(info);
			var playerObj = Runner.GetPlayerObject(source);
			if (playerObj == null) return;

			if (!IsWithinHostRange(playerObj.transform.position)) return;

			var player = playerObj.GetComponent<Player>();
			var inventory = playerObj.GetComponent<Inventory>();
			if (player == null || inventory == null) return;

			if (player.Money < offer.BuyPrice) return;
			if (InventoryOps.RoomFor(inventory.Slots, offer.Item.Id) < 1) return;

			if (!player.TrySpendMoney(offer.BuyPrice)) return;

			short leftover = inventory.TryAdd(offer.Item.Id, 1);
			if (leftover > 0)
			{
				// Should not happen — RoomFor pre-flighted — but refund just in case.
				player.AddMoney(offer.BuyPrice);
				return;
			}

			RemainingStock.Set(offerIndex, RemainingStock[offerIndex] - 1);
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_RequestSell(int slotIndex, RpcInfo info = default)
		{
			if (Runner == null) return;
			if (slotIndex < 0 || slotIndex >= Inventory.SlotCount) return;
			if (!IsAvailablePhase()) return;

			var source = ResolveSource(info);
			var playerObj = Runner.GetPlayerObject(source);
			if (playerObj == null) return;

			if (!IsWithinHostRange(playerObj.transform.position)) return;

			var player = playerObj.GetComponent<Player>();
			var inventory = playerObj.GetComponent<Inventory>();
			if (player == null || inventory == null) return;

			var slot = inventory.Slots[slotIndex];
			if (slot.IsEmpty) return;

			int unitPrice = GetSellPrice(slot.ItemId);
			if (unitPrice <= 0) return;

			if (!inventory.RemoveAt(slotIndex, 1)) return;
			player.AddMoney(unitPrice);
		}

		private static bool IsAvailablePhase()
		{
			// Shops are open during Day (Town) only. Lobby is allowed so pre-match
			// testing works without a MatchManager driving phase transitions.
			var mm = MatchManager.Instance;
			if (mm == null) return true;
			return mm.Phase == MatchPhase.Day || mm.Phase == MatchPhase.Lobby;
		}

		private PlayerRef ResolveSource(RpcInfo info)
		{
			return info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
		}

		private void OnStockChangedRender()
		{
			StockChanged?.Invoke();
		}
	}
}
