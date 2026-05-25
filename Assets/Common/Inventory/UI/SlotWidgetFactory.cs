using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.Common.Inventory.UI
{
	/// <summary>
	/// Builds the standard inventory slot UI primitive (bg + icon + count + hover relay).
	/// Used by both <c>InventoryHUD</c> (hotbar) and <c>LootUI</c> (loot panel grids).
	/// </summary>
	public static class SlotWidgetFactory
	{
		public struct Widget
		{
			public RectTransform Root;
			public Image Background;
			public Image Icon;
			public TMP_Text Label;
			public TMP_Text Count;
			public InventorySlotHover Hover;
		}

		public static Widget Build(Transform parent, string name, float size, Color normalColor, Color labelColor, bool showLabel)
		{
			var slot = new GameObject(name, typeof(RectTransform), typeof(Image));
			slot.transform.SetParent(parent, false);

			var srt = (RectTransform)slot.transform;
			srt.sizeDelta = new Vector2(size, size);

			var bg = slot.GetComponent<Image>();
			bg.color = normalColor;
			bg.raycastTarget = true;

			var hover = slot.AddComponent<InventorySlotHover>();

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

			TMP_Text label = null;
			if (showLabel)
			{
				var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
				labelGO.transform.SetParent(slot.transform, false);
				var lrt = (RectTransform)labelGO.transform;
				lrt.anchorMin = new Vector2(0f, 1f);
				lrt.anchorMax = new Vector2(0f, 1f);
				lrt.pivot = new Vector2(0f, 1f);
				lrt.anchoredPosition = new Vector2(4f, -2f);
				lrt.sizeDelta = new Vector2(20f, 18f);
				label = labelGO.GetComponent<TextMeshProUGUI>();
				label.alignment = TextAlignmentOptions.TopLeft;
				label.fontSize = 14f;
				label.color = labelColor;
				label.raycastTarget = false;
			}

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
			countText.color = labelColor;
			countText.raycastTarget = false;
			countText.text = string.Empty;

			return new Widget
			{
				Root = srt,
				Background = bg,
				Icon = iconImg,
				Label = label,
				Count = countText,
				Hover = hover,
			};
		}

		/// <summary>Fills the widget's icon + count for a single slot. Hides icon when empty.</summary>
		public static void ApplySlot(in Widget widget, InventorySlot slot)
		{
			if (slot.IsEmpty)
			{
				widget.Icon.enabled = false;
				widget.Icon.sprite = null;
				widget.Count.text = string.Empty;
				return;
			}

			var def = ItemDatabase.Instance != null ? ItemDatabase.Instance.GetById(slot.ItemId) : null;
			var sprite = def != null ? def.Icon : null;
			widget.Icon.sprite = sprite;
			widget.Icon.enabled = sprite != null;
			widget.Count.text = slot.Count > 1 ? $"x{slot.Count}" : string.Empty;
		}
	}
}
