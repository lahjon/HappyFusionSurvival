using UnityEngine;
using UnityEngine.EventSystems;

namespace Starter.Common.Inventory.UI
{
	/// <summary>
	/// Drop target on each loot/player slot. On drop: routes a move/swap via
	/// <see cref="LootSession.RequestMove"/>. Right-click: routes to
	/// <see cref="LootSession.RequestSwap"/> to push to the opposite inventory.
	/// </summary>
	public sealed class DroppableSlot : MonoBehaviour, IDropHandler, IPointerClickHandler
	{
		public SlotInventoryKind Kind;
		public int Index;

		public LootSession Session;

		public void OnDrop(PointerEventData eventData)
		{
			if (Session == null || eventData.pointerDrag == null) return;
			if (!eventData.pointerDrag.TryGetComponent<DraggableSlot>(out var src)) return;

			// Mark the drop as handled so DraggableSlot does not treat it as a drop-to-world.
			src.ConsumeDrop();

			if (src.Kind == Kind && src.Index == Index) return;

			Session.RequestMove((byte)src.Kind, (byte)src.Index, (byte)Kind, (byte)Index);
		}

		public void OnPointerClick(PointerEventData eventData)
		{
			if (Session == null) return;
			if (eventData.button != PointerEventData.InputButton.Right) return;

			Session.RequestSwap((byte)Kind, (byte)Index);
		}
	}
}
