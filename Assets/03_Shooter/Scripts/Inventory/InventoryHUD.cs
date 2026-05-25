using Starter.Common.Inventory;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only HUD for the local player's hotbar. Builds 8 slot widgets
	/// procedurally in Awake() so the scene only needs one empty UI GameObject
	/// with this component attached under a Canvas.
	/// </summary>
	public sealed class InventoryHUD : MonoBehaviour
	{
		[Header("Layout")]
		public float SlotSize = 72f;
		public float SlotSpacing = 6f;
		public Vector2 BottomOffset = new Vector2(0f, 24f);

		[Header("Colors")]
		public Color NormalColor = new Color(0f, 0f, 0f, 0.55f);
		public Color SelectedColor = new Color(1f, 0.85f, 0.1f, 0.85f);
		public Color LabelColor = Color.white;

		private Inventory _inventory;
		private LootSession _lootSession;
		private Image[] _slotBackgrounds;
		private Image[] _slotIcons;
		private TMP_Text[] _slotLabels;
		private TMP_Text[] _slotCounts;
		private InventorySlotHover[] _slotHovers;
		private InventoryTooltip _tooltip;
		private int _hoveredSlot = -1;
		private GameObject _slotsRoot;

		private void Awake()
		{
			BuildSlots();
		}

		private void Update()
		{
			if (_inventory != null)
				return;
			TryBind();
		}

		private void OnDestroy()
		{
			if (_inventory != null)
			{
				_inventory.SlotsChanged -= RefreshSlots;
				_inventory.SelectedChanged -= RefreshSelected;
			}
			if (_lootSession != null)
			{
				_lootSession.OpenedChanged -= OnLootOpenedChanged;
			}
		}

		private void BuildSlots()
		{
			var rt = transform as RectTransform;
			if (rt == null)
			{
				Debug.LogError("[InventoryHUD] Must live on a RectTransform (under a Canvas).");
				return;
			}

			_slotBackgrounds = new Image[Inventory.SlotCount];
			_slotIcons = new Image[Inventory.SlotCount];
			_slotLabels = new TMP_Text[Inventory.SlotCount];
			_slotCounts = new TMP_Text[Inventory.SlotCount];
			_slotHovers = new InventorySlotHover[Inventory.SlotCount];

			rt.anchorMin = new Vector2(0.5f, 0f);
			rt.anchorMax = new Vector2(0.5f, 0f);
			rt.pivot = new Vector2(0.5f, 0f);
			rt.anchoredPosition = BottomOffset;
			float totalW = Inventory.SlotCount * SlotSize + (Inventory.SlotCount - 1) * SlotSpacing;
			rt.sizeDelta = new Vector2(totalW, SlotSize);

			for (int i = 0; i < Inventory.SlotCount; i++)
			{
				var slot = new GameObject($"Slot_{i}", typeof(RectTransform), typeof(Image));
				slot.transform.SetParent(transform, false);
				var srt = (RectTransform)slot.transform;
				srt.anchorMin = new Vector2(0f, 0.5f);
				srt.anchorMax = new Vector2(0f, 0.5f);
				srt.pivot = new Vector2(0.5f, 0.5f);
				srt.sizeDelta = new Vector2(SlotSize, SlotSize);
				srt.anchoredPosition = new Vector2(SlotSize * 0.5f + i * (SlotSize + SlotSpacing), 0f);
				_slotBackgrounds[i] = slot.GetComponent<Image>();
				_slotBackgrounds[i].color = NormalColor;
				_slotBackgrounds[i].raycastTarget = true;

				var hover = slot.AddComponent<InventorySlotHover>();
				hover.SlotIndex = i;
				hover.Entered += OnSlotEntered;
				hover.Exited += OnSlotExited;
				_slotHovers[i] = hover;

				var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
				icon.transform.SetParent(slot.transform, false);
				var irt = (RectTransform)icon.transform;
				irt.anchorMin = Vector2.zero;
				irt.anchorMax = Vector2.one;
				irt.offsetMin = new Vector2(4f, 4f);
				irt.offsetMax = new Vector2(-4f, -4f);
				var iconImg = icon.GetComponent<Image>();
				iconImg.raycastTarget = false;
				iconImg.preserveAspect = true;
				iconImg.enabled = false;
				_slotIcons[i] = iconImg;

				var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
				label.transform.SetParent(slot.transform, false);
				var lrt = (RectTransform)label.transform;
				lrt.anchorMin = new Vector2(0f, 1f);
				lrt.anchorMax = new Vector2(0f, 1f);
				lrt.pivot = new Vector2(0f, 1f);
				lrt.anchoredPosition = new Vector2(4f, -2f);
				lrt.sizeDelta = new Vector2(20f, 18f);
				var text = label.GetComponent<TextMeshProUGUI>();
				text.alignment = TextAlignmentOptions.TopLeft;
				text.fontSize = 14f;
				text.color = LabelColor;
				text.raycastTarget = false;
				text.text = (i + 1).ToString();
				_slotLabels[i] = text;

				var countGO = new GameObject("Count", typeof(RectTransform), typeof(TextMeshProUGUI));
				countGO.transform.SetParent(slot.transform, false);
				var crt = (RectTransform)countGO.transform;
				crt.anchorMin = new Vector2(1f, 0f);
				crt.anchorMax = new Vector2(1f, 0f);
				crt.pivot = new Vector2(1f, 0f);
				crt.anchoredPosition = new Vector2(-4f, 2f);
				crt.sizeDelta = new Vector2(30f, 18f);
				var countText = countGO.GetComponent<TextMeshProUGUI>();
				countText.alignment = TextAlignmentOptions.BottomRight;
				countText.fontSize = 14f;
				countText.color = LabelColor;
				countText.raycastTarget = false;
				countText.text = string.Empty;
				_slotCounts[i] = countText;
			}

			BuildTooltip();
		}

		private void BuildTooltip()
		{
			var canvas = GetComponentInParent<Canvas>();
			Transform parent = canvas != null ? canvas.transform : transform;

			var go = new GameObject("InventoryTooltip", typeof(RectTransform));
			go.transform.SetParent(parent, false);
			go.transform.SetAsLastSibling();
			_tooltip = go.AddComponent<InventoryTooltip>();
		}

		private void OnSlotEntered(int slotIndex)
		{
			_hoveredSlot = slotIndex;
			ShowTooltipForSlot(slotIndex);
		}

		private void OnSlotExited(int slotIndex)
		{
			if (_hoveredSlot != slotIndex)
				return;
			_hoveredSlot = -1;
			if (_tooltip != null)
				_tooltip.Hide();
		}

		private void ShowTooltipForSlot(int slotIndex)
		{
			if (_tooltip == null) return;
			if (_inventory == null || slotIndex < 0 || slotIndex >= Inventory.SlotCount)
			{
				_tooltip.Hide();
				return;
			}

			var slot = _inventory.Slots[slotIndex];
			if (slot.IsEmpty || ItemDatabase.Instance == null)
			{
				_tooltip.Hide();
				return;
			}

			var def = ItemDatabase.Instance.GetById(slot.ItemId);
			if (def == null)
			{
				_tooltip.Hide();
				return;
			}

			_tooltip.Show(def, slot.Count);
		}

		private void TryBind()
		{
			var gm = FindFirstObjectByType<GameManager>();
			if (gm == null || gm.LocalPlayer == null)
				return;

			var inv = gm.LocalPlayer.GetComponent<Inventory>();
			if (inv == null)
				return;

			_inventory = inv;
			_inventory.SlotsChanged += RefreshSlots;
			_inventory.SelectedChanged += RefreshSelected;
			RefreshSlots();
			RefreshSelected();

			_lootSession = gm.LocalPlayer.GetComponent<LootSession>();
			if (_lootSession != null)
			{
				_lootSession.OpenedChanged += OnLootOpenedChanged;
			}
		}

		private void OnLootOpenedChanged(LootContainer container)
		{
			SetVisible(container == null);
		}

		private void SetVisible(bool visible)
		{
			if (_slotBackgrounds == null) return;
			for (int i = 0; i < _slotBackgrounds.Length; i++)
			{
				if (_slotBackgrounds[i] == null) continue;
				_slotBackgrounds[i].gameObject.SetActive(visible);
			}
			if (_tooltip != null && !visible) _tooltip.Hide();
		}

		private void RefreshSlots()
		{
			if (_inventory == null || _slotLabels == null)
				return;

			for (int i = 0; i < Inventory.SlotCount; i++)
			{
				var slot = _inventory.Slots[i];
				_slotLabels[i].text = (i + 1).ToString();

				if (slot.IsEmpty)
				{
					_slotIcons[i].enabled = false;
					_slotIcons[i].sprite = null;
					_slotCounts[i].text = string.Empty;
				}
				else
				{
					var def = ItemDatabase.Instance != null ? ItemDatabase.Instance.GetById(slot.ItemId) : null;
					var sprite = def != null ? def.Icon : null;
					_slotIcons[i].sprite = sprite;
					_slotIcons[i].enabled = sprite != null;
					_slotCounts[i].text = slot.Count > 1 ? $"x{slot.Count}" : string.Empty;
				}
			}

			if (_hoveredSlot != -1)
				ShowTooltipForSlot(_hoveredSlot);
		}

		private void RefreshSelected()
		{
			if (_inventory == null || _slotBackgrounds == null)
				return;

			for (int i = 0; i < Inventory.SlotCount; i++)
			{
				_slotBackgrounds[i].color = (i == _inventory.SelectedSlot) ? SelectedColor : NormalColor;
			}
		}
	}
}
