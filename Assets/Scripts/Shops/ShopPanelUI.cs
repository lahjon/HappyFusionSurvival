using Starter.Common.Inventory;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only shopkeeper menu. Mirrors <see cref="QuestPanelUI"/>'s procedural build style:
	/// attach to one RectTransform under a Canvas, the rest is generated at runtime. Two columns —
	/// left = the shop's 4 buy offers, right = the player's 8 hotbar slots as sell candidates —
	/// with the player's money displayed in the header.
	/// </summary>
	public sealed class ShopPanelUI : MonoBehaviour
	{
		private const int BuyRows = Shopkeeper.MaxOffers;
		private const int SellRows = Inventory.SlotCount;

		[Header("Layout")]
		public Vector2 PanelSize = new Vector2(820f, 480f);
		public float RowHeight = 56f;
		public float RowSpacing = 4f;

		[Header("Colors")]
		public Color PanelColor = new Color(0f, 0f, 0f, 0.85f);
		public Color HeaderColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
		public Color ColumnHeaderColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);
		public Color RowAvailable = new Color(0.12f, 0.12f, 0.12f, 0.9f);
		public Color RowDisabled = new Color(0.18f, 0.05f, 0.05f, 0.9f);
		public Color RowSoldOut = new Color(0.1f, 0.1f, 0.1f, 0.6f);
		public Color RowEmpty = new Color(0.08f, 0.08f, 0.08f, 0.6f);
		public Color ButtonEnabled = new Color(0.15f, 0.65f, 0.2f, 1f);
		public Color ButtonDisabled = new Color(0.25f, 0.25f, 0.25f, 0.8f);
		public Color TextColor = Color.white;
		public Color TextMuted = new Color(0.7f, 0.7f, 0.7f, 1f);
		public Color TextMissing = new Color(1f, 0.5f, 0.5f, 1f);
		public Color TextMoney = new Color(1f, 0.9f, 0.4f, 1f);

		private ShopSession _session;
		private Inventory _inventory;
		private Player _player;

		private GameObject _root;
		private TMP_Text _headerLabel;
		private TMP_Text _moneyLabel;
		private BuyRowWidget[] _buyRows = new BuyRowWidget[BuyRows];
		private SellRowWidget[] _sellRows = new SellRowWidget[SellRows];
		private TMP_Text _emptyOffersHint;

		private struct BuyRowWidget
		{
			public GameObject Root;
			public Image Background;
			public Image Icon;
			public TMP_Text Title;
			public TMP_Text PriceLabel;
			public TMP_Text StockLabel;
			public Image ButtonBg;
			public Button Button;
			public TMP_Text ButtonLabel;
		}

		private struct SellRowWidget
		{
			public GameObject Root;
			public Image Background;
			public Image Icon;
			public TMP_Text Title;
			public TMP_Text PriceLabel;
			public Image ButtonBg;
			public Button Button;
			public TMP_Text ButtonLabel;
		}

		private void Awake()
		{
			BuildLayout();
			SetVisible(false);
		}

		private void Update()
		{
			if (_session == null)
			{
				TryBind();
				return;
			}

			if (_session.Current != null)
			{
				RefreshAll();
			}
		}

		private void OnDestroy()
		{
			Unbind();
		}

		private void TryBind()
		{
			var gm = FindAnyObjectByType<GameManager>();
			if (gm == null || gm.LocalPlayer == null) return;

			var session = gm.LocalPlayer.GetComponent<ShopSession>();
			var inv = gm.LocalPlayer.GetComponent<Inventory>();
			if (session == null || inv == null) return;

			_session = session;
			_inventory = inv;
			_player = gm.LocalPlayer;

			_session.OpenedChanged += OnOpenedChanged;
		}

		private void Unbind()
		{
			if (_session != null) _session.OpenedChanged -= OnOpenedChanged;
			_session = null;
			_inventory = null;
			_player = null;
		}

		private void OnOpenedChanged(Shopkeeper shop)
		{
			SetVisible(shop != null);
			if (shop == null) return;

			_headerLabel.text = shop.DisplayName;
			RebuildRows(shop);
		}

		private void SetVisible(bool visible)
		{
			if (_root != null) _root.SetActive(visible);
		}

		private void RebuildRows(Shopkeeper shop)
		{
			int count = shop.OfferCount;
			for (int i = 0; i < BuyRows; i++)
			{
				_buyRows[i].Root.SetActive(i < count);
			}
			if (_emptyOffersHint != null) _emptyOffersHint.gameObject.SetActive(count == 0);
			RefreshAll();
		}

		private void RefreshAll()
		{
			var shop = _session.Current;
			if (shop == null) return;

			if (_moneyLabel != null && _player != null)
			{
				_moneyLabel.text = $"${_player.Money}";
			}

			RefreshBuyRows(shop);
			RefreshSellRows(shop);
		}

		private void RefreshBuyRows(Shopkeeper shop)
		{
			int playerMoney = _player != null ? _player.Money : 0;
			int count = shop.OfferCount;
			for (int i = 0; i < count; i++)
			{
				var offer = shop.GetOffer(i);
				var row = _buyRows[i];
				if (offer.Item == null)
				{
					row.Root.SetActive(false);
					continue;
				}

				row.Icon.sprite = offer.Item.Icon;
				row.Icon.enabled = offer.Item.Icon != null;
				row.Title.text = offer.Item.DisplayName;
				row.PriceLabel.text = $"${offer.BuyPrice}";

				int stock = shop.RemainingStock[i];
				bool hasRoom = _inventory != null && InventoryOps.RoomFor(_inventory.Slots, offer.Item.Id) >= 1;
				bool canAfford = playerMoney >= offer.BuyPrice;
				bool inStock = stock > 0;

				row.StockLabel.text = inStock ? $"{stock} left" : "SOLD OUT";
				row.StockLabel.color = inStock ? TextMuted : TextMissing;

				if (!inStock)
				{
					row.Background.color = RowSoldOut;
					SetButton(row.ButtonBg, row.Button, row.ButtonLabel, false, "SOLD OUT");
				}
				else if (!canAfford)
				{
					row.Background.color = RowDisabled;
					SetButton(row.ButtonBg, row.Button, row.ButtonLabel, false, "NO FUNDS");
				}
				else if (!hasRoom)
				{
					row.Background.color = RowDisabled;
					SetButton(row.ButtonBg, row.Button, row.ButtonLabel, false, "NO ROOM");
				}
				else
				{
					row.Background.color = RowAvailable;
					SetButton(row.ButtonBg, row.Button, row.ButtonLabel, true, "BUY");
				}
			}
		}

		private void RefreshSellRows(Shopkeeper shop)
		{
			for (int i = 0; i < SellRows; i++)
			{
				var row = _sellRows[i];
				var slot = _inventory != null ? _inventory.Slots[i] : InventorySlot.Empty;

				if (slot.IsEmpty || ItemDatabase.Instance == null)
				{
					row.Background.color = RowEmpty;
					row.Icon.enabled = false;
					row.Title.text = $"<color=#{ColorUtility.ToHtmlStringRGB(TextMuted)}>Slot {i + 1}: (empty)</color>";
					row.PriceLabel.text = string.Empty;
					SetButton(row.ButtonBg, row.Button, row.ButtonLabel, false, "—");
					continue;
				}

				var def = ItemDatabase.Instance.GetById(slot.ItemId);
				if (def == null)
				{
					row.Background.color = RowEmpty;
					row.Icon.enabled = false;
					row.Title.text = "(unknown)";
					row.PriceLabel.text = string.Empty;
					SetButton(row.ButtonBg, row.Button, row.ButtonLabel, false, "—");
					continue;
				}

				row.Icon.sprite = def.Icon;
				row.Icon.enabled = def.Icon != null;
				row.Title.text = $"{def.DisplayName} x{slot.Count}";

				int unitPrice = shop.GetSellPrice(slot.ItemId);
				if (unitPrice <= 0)
				{
					row.Background.color = RowDisabled;
					row.PriceLabel.text = "—";
					SetButton(row.ButtonBg, row.Button, row.ButtonLabel, false, "WON'T BUY");
				}
				else
				{
					row.Background.color = RowAvailable;
					row.PriceLabel.text = $"${unitPrice}";
					SetButton(row.ButtonBg, row.Button, row.ButtonLabel, true, "SELL 1");
				}
			}
		}

		private void SetButton(Image bg, Button button, TMP_Text label, bool enabled, string text)
		{
			button.interactable = enabled;
			bg.color = enabled ? ButtonEnabled : ButtonDisabled;
			label.text = text;
		}

		private void OnBuyClicked(int offerIndex)
		{
			if (_session == null) return;
			_session.RequestBuy(offerIndex);
		}

		private void OnSellClicked(int slotIndex)
		{
			if (_session == null) return;
			_session.RequestSell(slotIndex);
		}

		// --- Layout build ---

		private void BuildLayout()
		{
			var rt = transform as RectTransform;
			if (rt == null)
			{
				Debug.LogError("[ShopPanelUI] Must live on a RectTransform (under a Canvas).");
				return;
			}

			rt.anchorMin = new Vector2(0.5f, 0.5f);
			rt.anchorMax = new Vector2(0.5f, 0.5f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.anchoredPosition = Vector2.zero;
			rt.sizeDelta = PanelSize;

			_root = new GameObject("ShopPanel", typeof(RectTransform), typeof(Image));
			_root.transform.SetParent(transform, false);
			var panelRT = (RectTransform)_root.transform;
			panelRT.anchorMin = Vector2.zero;
			panelRT.anchorMax = Vector2.one;
			panelRT.offsetMin = Vector2.zero;
			panelRT.offsetMax = Vector2.zero;
			_root.GetComponent<Image>().color = PanelColor;

			// Header bar: shop name + money
			var header = new GameObject("Header", typeof(RectTransform), typeof(Image));
			header.transform.SetParent(_root.transform, false);
			var hrt = (RectTransform)header.transform;
			hrt.anchorMin = new Vector2(0f, 1f);
			hrt.anchorMax = new Vector2(1f, 1f);
			hrt.pivot = new Vector2(0.5f, 1f);
			hrt.anchoredPosition = Vector2.zero;
			hrt.sizeDelta = new Vector2(0f, 36f);
			header.GetComponent<Image>().color = HeaderColor;

			_headerLabel = AddText(header.transform, "HeaderLabel", "Shopkeeper", 18, TextAlignmentOptions.Left,
				new Vector2(0f, 0f), new Vector2(0.6f, 1f));
			_headerLabel.rectTransform.offsetMin = new Vector2(12f, 0f);
			_headerLabel.rectTransform.offsetMax = Vector2.zero;

			_moneyLabel = AddText(header.transform, "MoneyLabel", "$0", 18, TextAlignmentOptions.Right,
				new Vector2(0.6f, 0f), new Vector2(1f, 1f));
			_moneyLabel.color = TextMoney;
			_moneyLabel.fontStyle = FontStyles.Bold;
			_moneyLabel.rectTransform.offsetMin = Vector2.zero;
			_moneyLabel.rectTransform.offsetMax = new Vector2(-12f, 0f);

			// Two columns under the header
			BuildBuyColumn(_root.transform);
			BuildSellColumn(_root.transform);
		}

		private void BuildBuyColumn(Transform parent)
		{
			var col = new GameObject("BuyColumn", typeof(RectTransform));
			col.transform.SetParent(parent, false);
			var crt = (RectTransform)col.transform;
			crt.anchorMin = new Vector2(0f, 0f);
			crt.anchorMax = new Vector2(0.5f, 1f);
			crt.pivot = new Vector2(0.5f, 0.5f);
			crt.offsetMin = new Vector2(8f, 8f);
			crt.offsetMax = new Vector2(-4f, -42f);

			var colHeader = new GameObject("BuyHeader", typeof(RectTransform), typeof(Image));
			colHeader.transform.SetParent(col.transform, false);
			var chrt = (RectTransform)colHeader.transform;
			chrt.anchorMin = new Vector2(0f, 1f);
			chrt.anchorMax = new Vector2(1f, 1f);
			chrt.pivot = new Vector2(0.5f, 1f);
			chrt.anchoredPosition = Vector2.zero;
			chrt.sizeDelta = new Vector2(0f, 26f);
			colHeader.GetComponent<Image>().color = ColumnHeaderColor;
			AddText(colHeader.transform, "Label", "FOR SALE", 14, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);

			var list = new GameObject("BuyList", typeof(RectTransform));
			list.transform.SetParent(col.transform, false);
			var lrt = (RectTransform)list.transform;
			lrt.anchorMin = Vector2.zero;
			lrt.anchorMax = Vector2.one;
			lrt.pivot = new Vector2(0.5f, 1f);
			lrt.offsetMin = Vector2.zero;
			lrt.offsetMax = new Vector2(0f, -30f);

			for (int i = 0; i < BuyRows; i++)
			{
				_buyRows[i] = BuildBuyRow(list.transform, i);
			}

			_emptyOffersHint = AddText(list.transform, "EmptyHint", "No items for sale", 14, TextAlignmentOptions.Center,
				new Vector2(0f, 1f), new Vector2(1f, 1f));
			var ehrt = _emptyOffersHint.rectTransform;
			ehrt.pivot = new Vector2(0.5f, 1f);
			ehrt.anchoredPosition = new Vector2(0f, -10f);
			ehrt.sizeDelta = new Vector2(0f, 24f);
			_emptyOffersHint.gameObject.SetActive(false);
		}

		private void BuildSellColumn(Transform parent)
		{
			var col = new GameObject("SellColumn", typeof(RectTransform));
			col.transform.SetParent(parent, false);
			var crt = (RectTransform)col.transform;
			crt.anchorMin = new Vector2(0.5f, 0f);
			crt.anchorMax = new Vector2(1f, 1f);
			crt.pivot = new Vector2(0.5f, 0.5f);
			crt.offsetMin = new Vector2(4f, 8f);
			crt.offsetMax = new Vector2(-8f, -42f);

			var colHeader = new GameObject("SellHeader", typeof(RectTransform), typeof(Image));
			colHeader.transform.SetParent(col.transform, false);
			var chrt = (RectTransform)colHeader.transform;
			chrt.anchorMin = new Vector2(0f, 1f);
			chrt.anchorMax = new Vector2(1f, 1f);
			chrt.pivot = new Vector2(0.5f, 1f);
			chrt.anchoredPosition = Vector2.zero;
			chrt.sizeDelta = new Vector2(0f, 26f);
			colHeader.GetComponent<Image>().color = ColumnHeaderColor;
			AddText(colHeader.transform, "Label", "YOUR INVENTORY", 14, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);

			var list = new GameObject("SellList", typeof(RectTransform));
			list.transform.SetParent(col.transform, false);
			var lrt = (RectTransform)list.transform;
			lrt.anchorMin = Vector2.zero;
			lrt.anchorMax = Vector2.one;
			lrt.pivot = new Vector2(0.5f, 1f);
			lrt.offsetMin = Vector2.zero;
			lrt.offsetMax = new Vector2(0f, -30f);

			for (int i = 0; i < SellRows; i++)
			{
				_sellRows[i] = BuildSellRow(list.transform, i);
			}
		}

		private BuyRowWidget BuildBuyRow(Transform parent, int index)
		{
			var row = new GameObject($"Buy_{index}", typeof(RectTransform), typeof(Image));
			row.transform.SetParent(parent, false);
			var rrt = (RectTransform)row.transform;
			rrt.anchorMin = new Vector2(0f, 1f);
			rrt.anchorMax = new Vector2(1f, 1f);
			rrt.pivot = new Vector2(0f, 1f);
			rrt.anchoredPosition = new Vector2(0f, -index * (RowHeight + RowSpacing));
			rrt.sizeDelta = new Vector2(0f, RowHeight);
			var bg = row.GetComponent<Image>();
			bg.color = RowAvailable;

			var icon = BuildIcon(row.transform);

			var title = AddText(row.transform, "Title", string.Empty, 14, TextAlignmentOptions.TopLeft,
				new Vector2(0f, 0f), new Vector2(1f, 1f));
			title.fontStyle = FontStyles.Bold;
			var trt = title.rectTransform;
			trt.pivot = new Vector2(0f, 1f);
			trt.anchoredPosition = new Vector2(56f, -6f);
			trt.sizeDelta = new Vector2(-220f, 20f);

			var price = AddText(row.transform, "Price", string.Empty, 14, TextAlignmentOptions.TopLeft,
				new Vector2(0f, 0f), new Vector2(1f, 1f));
			price.color = TextMoney;
			var prt = price.rectTransform;
			prt.pivot = new Vector2(0f, 1f);
			prt.anchoredPosition = new Vector2(56f, -26f);
			prt.sizeDelta = new Vector2(120f, 20f);

			var stock = AddText(row.transform, "Stock", string.Empty, 12, TextAlignmentOptions.TopLeft,
				new Vector2(0f, 0f), new Vector2(1f, 1f));
			stock.color = TextMuted;
			var srt = stock.rectTransform;
			srt.pivot = new Vector2(0f, 1f);
			srt.anchoredPosition = new Vector2(176f, -26f);
			srt.sizeDelta = new Vector2(80f, 20f);

			BuildRowButton(row.transform, out Image btnBg, out Button button, out TMP_Text btnLabel, "BUY");
			int captured = index;
			button.onClick.AddListener(() => OnBuyClicked(captured));

			return new BuyRowWidget
			{
				Root = row,
				Background = bg,
				Icon = icon,
				Title = title,
				PriceLabel = price,
				StockLabel = stock,
				ButtonBg = btnBg,
				Button = button,
				ButtonLabel = btnLabel,
			};
		}

		private SellRowWidget BuildSellRow(Transform parent, int index)
		{
			var row = new GameObject($"Sell_{index}", typeof(RectTransform), typeof(Image));
			row.transform.SetParent(parent, false);
			var rrt = (RectTransform)row.transform;
			rrt.anchorMin = new Vector2(0f, 1f);
			rrt.anchorMax = new Vector2(1f, 1f);
			rrt.pivot = new Vector2(0f, 1f);
			rrt.anchoredPosition = new Vector2(0f, -index * (RowHeight + RowSpacing));
			rrt.sizeDelta = new Vector2(0f, RowHeight);
			var bg = row.GetComponent<Image>();
			bg.color = RowEmpty;

			var icon = BuildIcon(row.transform);

			var title = AddText(row.transform, "Title", string.Empty, 14, TextAlignmentOptions.TopLeft,
				new Vector2(0f, 0f), new Vector2(1f, 1f));
			var trt = title.rectTransform;
			trt.pivot = new Vector2(0f, 1f);
			trt.anchoredPosition = new Vector2(56f, -8f);
			trt.sizeDelta = new Vector2(-220f, 20f);

			var price = AddText(row.transform, "Price", string.Empty, 14, TextAlignmentOptions.TopLeft,
				new Vector2(0f, 0f), new Vector2(1f, 1f));
			price.color = TextMoney;
			var prt = price.rectTransform;
			prt.pivot = new Vector2(0f, 1f);
			prt.anchoredPosition = new Vector2(56f, -28f);
			prt.sizeDelta = new Vector2(140f, 20f);

			BuildRowButton(row.transform, out Image btnBg, out Button button, out TMP_Text btnLabel, "SELL 1");
			int captured = index;
			button.onClick.AddListener(() => OnSellClicked(captured));

			return new SellRowWidget
			{
				Root = row,
				Background = bg,
				Icon = icon,
				Title = title,
				PriceLabel = price,
				ButtonBg = btnBg,
				Button = button,
				ButtonLabel = btnLabel,
			};
		}

		private Image BuildIcon(Transform parent)
		{
			var go = new GameObject("Icon", typeof(RectTransform), typeof(Image));
			go.transform.SetParent(parent, false);
			var rt = (RectTransform)go.transform;
			rt.anchorMin = new Vector2(0f, 0.5f);
			rt.anchorMax = new Vector2(0f, 0.5f);
			rt.pivot = new Vector2(0f, 0.5f);
			rt.anchoredPosition = new Vector2(8f, 0f);
			rt.sizeDelta = new Vector2(40f, 40f);
			var img = go.GetComponent<Image>();
			img.preserveAspect = true;
			img.raycastTarget = false;
			img.enabled = false;
			return img;
		}

		private void BuildRowButton(Transform parent, out Image bg, out Button button, out TMP_Text label, string defaultText)
		{
			var btnGO = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
			btnGO.transform.SetParent(parent, false);
			var brt = (RectTransform)btnGO.transform;
			brt.anchorMin = new Vector2(1f, 0.5f);
			brt.anchorMax = new Vector2(1f, 0.5f);
			brt.pivot = new Vector2(1f, 0.5f);
			brt.anchoredPosition = new Vector2(-8f, 0f);
			brt.sizeDelta = new Vector2(110f, 36f);
			bg = btnGO.GetComponent<Image>();
			bg.color = ButtonDisabled;
			button = btnGO.GetComponent<Button>();

			label = AddText(btnGO.transform, "Label", defaultText, 13, TextAlignmentOptions.Center,
				Vector2.zero, Vector2.one);
			label.raycastTarget = false;
		}

		private TMP_Text AddText(Transform parent, string name, string text, float fontSize,
			TextAlignmentOptions alignment, Vector2 anchorMin, Vector2 anchorMax)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
			go.transform.SetParent(parent, false);
			var t = go.GetComponent<TextMeshProUGUI>();
			t.text = text;
			t.fontSize = fontSize;
			t.alignment = alignment;
			t.color = TextColor;
			t.raycastTarget = false;
			var rt = t.rectTransform;
			rt.anchorMin = anchorMin;
			rt.anchorMax = anchorMax;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;
			return t;
		}
	}
}
