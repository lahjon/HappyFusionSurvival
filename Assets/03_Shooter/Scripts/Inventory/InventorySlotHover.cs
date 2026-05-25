using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Starter.Shooter
{
	/// <summary>
	/// Pointer-enter/exit relay attached to each inventory slot. Notifies the
	/// owning InventoryHUD which slot index the cursor is currently over.
	/// </summary>
	public sealed class InventorySlotHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
	{
		public int SlotIndex;

		public event Action<int> Entered;
		public event Action<int> Exited;

		public void OnPointerEnter(PointerEventData eventData) => Entered?.Invoke(SlotIndex);
		public void OnPointerExit(PointerEventData eventData) => Exited?.Invoke(SlotIndex);

		private void OnDisable() => Exited?.Invoke(SlotIndex);
	}
}
