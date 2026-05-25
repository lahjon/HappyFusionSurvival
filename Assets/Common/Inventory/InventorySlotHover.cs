using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// Pointer-enter/exit relay attached to each inventory slot. Notifies the
	/// owning HUD which slot index the cursor is currently over, and applies a
	/// local hover feedback (slight scale + brighten) on the slot background.
	/// </summary>
	public sealed class InventorySlotHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
	{
		public int SlotIndex;

		[Header("Hover feedback")]
		public Image TargetImage;
		public float HoverScale = 1.08f;
		public float HoverBrighten = 0.18f;

		public event Action<int> Entered;
		public event Action<int> Exited;

		private Vector3 _baseScale;
		private Color _baseColor;
		private bool _hovered;

		private void Awake()
		{
			if (TargetImage == null) TargetImage = GetComponent<Image>();
			_baseScale = transform.localScale;
			_baseColor = TargetImage != null ? TargetImage.color : Color.white;
		}

		/// <summary>
		/// Update the slot's resting color. Owners that swap the slot tint dynamically
		/// (e.g. hotbar selection highlight) should call this instead of writing to the
		/// image directly, so the hover feedback stays in sync.
		/// </summary>
		public void SetBaseColor(Color color)
		{
			_baseColor = color;
			ApplyVisualState();
		}

		public void OnPointerEnter(PointerEventData eventData)
		{
			_hovered = true;
			ApplyVisualState();
			Entered?.Invoke(SlotIndex);
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			if (_hovered)
			{
				_hovered = false;
				ApplyVisualState();
			}
			Exited?.Invoke(SlotIndex);
		}

		private void OnDisable()
		{
			if (_hovered)
			{
				_hovered = false;
				ApplyVisualState();
			}
			Exited?.Invoke(SlotIndex);
		}

		private void ApplyVisualState()
		{
			transform.localScale = _hovered ? _baseScale * HoverScale : _baseScale;
			if (TargetImage != null)
			{
				if (_hovered)
				{
					var c = Color.Lerp(_baseColor, Color.white, HoverBrighten);
					c.a = _baseColor.a;
					TargetImage.color = c;
				}
				else
				{
					TargetImage.color = _baseColor;
				}
			}
		}
	}
}
