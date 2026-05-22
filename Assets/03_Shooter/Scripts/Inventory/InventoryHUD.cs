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
		private Image[] _slotBackgrounds;
		private TMP_Text[] _slotLabels;

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
			if (_inventory == null)
				return;
			_inventory.SlotsChanged -= RefreshSlots;
			_inventory.SelectedChanged -= RefreshSelected;
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
			_slotLabels = new TMP_Text[Inventory.SlotCount];

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
				_slotBackgrounds[i].raycastTarget = false;

				var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
				label.transform.SetParent(slot.transform, false);
				var lrt = (RectTransform)label.transform;
				lrt.anchorMin = Vector2.zero;
				lrt.anchorMax = Vector2.one;
				lrt.offsetMin = Vector2.zero;
				lrt.offsetMax = Vector2.zero;
				var text = label.GetComponent<TextMeshProUGUI>();
				text.alignment = TextAlignmentOptions.Center;
				text.fontSize = 14f;
				text.color = LabelColor;
				text.raycastTarget = false;
				text.text = (i + 1).ToString();
				_slotLabels[i] = text;
			}
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
		}

		private void RefreshSlots()
		{
			if (_inventory == null || _slotLabels == null)
				return;

			for (int i = 0; i < Inventory.SlotCount; i++)
			{
				var slot = _inventory.Slots[i];
				if (slot.IsEmpty)
				{
					_slotLabels[i].text = (i + 1).ToString();
				}
				else
				{
					var def = ItemDatabase.Instance != null ? ItemDatabase.Instance.GetById(slot.ItemId) : null;
					string itemName = def != null ? def.DisplayName : $"#{slot.ItemId}";
					_slotLabels[i].text = $"{i + 1}\n{itemName}\nx{slot.Count}";
				}
			}
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
