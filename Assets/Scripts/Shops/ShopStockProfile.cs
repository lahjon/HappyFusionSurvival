using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Per-vendor procedural-stock config. Separates the reusable item pools (a shared
	/// <see cref="LootTableSet"/>) from a vendor's own tuning — how many offers to roll, how much
	/// stock per offer, and the price markup. Assign one to a <see cref="Shopkeeper"/> to have it
	/// roll its stock at spawn instead of using a hand-authored offer list.
	/// </summary>
	[CreateAssetMenu(fileName = "ShopStockProfile", menuName = "Shooter/Shop Stock Profile", order = 0)]
	public sealed class ShopStockProfile : ScriptableObject
	{
		[Tooltip("Weighted bundle of loot tables the shop draws its offers from.")]
		public LootTableSet TableSet;

		[Header("Offer count")]
		[Tooltip("Minimum number of distinct offers to roll (clamped to Shopkeeper.MaxOffers).")]
		[Min(0)] public int MinOffers = 3;
		[Tooltip("Maximum number of distinct offers to roll (clamped to Shopkeeper.MaxOffers).")]
		[Min(0)] public int MaxOffers = 4;

		[Tooltip("If on, the shop avoids stocking the same item twice (rerolls duplicates).")]
		public bool UniqueItems = true;

		[Header("Stock")]
		[Tooltip("Starting stock rolled per offer (inclusive range).")]
		public Vector2Int StockRange = new Vector2Int(1, 5);

		[Header("Pricing")]
		[Tooltip("Buy price = item BaseValue * this percent / 100. 100 = sell at face value, 150 = +50% markup.")]
		[Range(1, 1000)] public int BuyMarkupPercent = 100;

		/// <summary>Buy price for one unit of the given item under this profile's markup (min 1).</summary>
		public int BuyPriceFor(ItemData item)
		{
			if (item == null) return 0;
			return Mathf.Max(1, (item.BaseValue * BuyMarkupPercent) / 100);
		}
	}
}
