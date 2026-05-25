using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Starter.Common.Inventory.UI
{
	public enum SlotInventoryKind : byte
	{
		Player = LootContainer.InvPlayer,
		Container = LootContainer.InvContainer,
	}

	/// <summary>
	/// Drag source on each loot/player slot in the LootUI. Lifts a ghost icon to
	/// the drag layer for the duration of the drag; the actual mutation happens
	/// on drop via <see cref="DroppableSlot"/>.
	/// </summary>
	public sealed class DraggableSlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
	{
		public SlotInventoryKind Kind;
		public int Index;

		/// <summary>Source icon shown in the slot (cloned to the ghost on begin-drag).</summary>
		public Image SourceIcon;

		/// <summary>Layer that hosts the dragging ghost — must have raycast off.</summary>
		public RectTransform GhostLayer;

		private GameObject _ghost;
		private Image _ghostIcon;

		public void OnBeginDrag(PointerEventData eventData)
		{
			if (SourceIcon == null || SourceIcon.enabled == false || SourceIcon.sprite == null)
				return;
			if (GhostLayer == null)
				return;

			_ghost = new GameObject("DragGhost", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
			_ghost.transform.SetParent(GhostLayer, false);

			var grt = (RectTransform)_ghost.transform;
			grt.sizeDelta = ((RectTransform)SourceIcon.transform).rect.size;

			_ghostIcon = _ghost.GetComponent<Image>();
			_ghostIcon.sprite = SourceIcon.sprite;
			_ghostIcon.preserveAspect = true;
			_ghostIcon.raycastTarget = false;

			var cg = _ghost.GetComponent<CanvasGroup>();
			cg.blocksRaycasts = false;
			cg.alpha = 0.85f;

			MoveGhostTo(eventData);
		}

		public void OnDrag(PointerEventData eventData)
		{
			if (_ghost == null) return;
			MoveGhostTo(eventData);
		}

		public void OnEndDrag(PointerEventData eventData)
		{
			if (_ghost != null)
			{
				Destroy(_ghost);
				_ghost = null;
				_ghostIcon = null;
			}
		}

		private void MoveGhostTo(PointerEventData eventData)
		{
			if (_ghost == null) return;

			var ghostRT = (RectTransform)_ghost.transform;
			var parent = GhostLayer;

			RectTransformUtility.ScreenPointToLocalPointInRectangle(
				parent, eventData.position, eventData.pressEventCamera, out var localPoint);

			ghostRT.anchoredPosition = localPoint;
		}
	}
}
