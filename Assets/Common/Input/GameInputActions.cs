using UnityEngine;
using UnityEngine.InputSystem;

namespace Starter.Common.Input
{
	/// <summary>
	/// Local-only owner of the InputActionAsset for a single player. Lives on the
	/// player root alongside PlayerInput / Inventory / InputContextController.
	/// Other components grab a reference via <see cref="GetComponent{T}"/> and read
	/// actions directly.
	/// </summary>
	public sealed class GameInputActions : MonoBehaviour
	{
		[SerializeField] private InputActionAsset _referenceAsset;

		public InputActionAsset Asset { get; private set; }
		public InputActionMap PlayerMap { get; private set; }
		public InputActionMap InventoryMap { get; private set; }

		public InputAction Move { get; private set; }
		public InputAction Look { get; private set; }
		public InputAction Fire { get; private set; }
		public InputAction AltAttack { get; private set; }
		public InputAction Jump { get; private set; }
		public InputAction Sprint { get; private set; }
		public InputAction Crouch { get; private set; }
		public InputAction Interact { get; private set; }
		public InputAction Drop { get; private set; }
		public InputAction Rotate { get; private set; }
		public InputAction HotbarScroll { get; private set; }
		public InputAction[] HotbarSlots { get; private set; }
		public InputAction Emotes { get; private set; }

		public InputAction Close { get; private set; }
		public InputAction TakeAll { get; private set; }
		public InputAction Point { get; private set; }
		public InputAction LeftClick { get; private set; }
		public InputAction RightClick { get; private set; }

		public bool IsInitialized => Asset != null;

		public void EnableForLocalPlayer()
		{
			if (IsInitialized)
				return;

			if (_referenceAsset == null)
			{
				Debug.LogError("[GameInputActions] ReferenceAsset is not assigned.", this);
				return;
			}

			Asset = Instantiate(_referenceAsset);

			PlayerMap = Asset.FindActionMap("Player", true);
			InventoryMap = Asset.FindActionMap("Inventory", true);

			Move = PlayerMap.FindAction("Move", true);
			Look = PlayerMap.FindAction("Look", true);
			Fire = PlayerMap.FindAction("Attack", true);
			// Secondary attack / aim — RMB (gamepad left trigger). Player-map "AltAttack",
			// distinct from the Inventory/UI maps' "RightClick".
			AltAttack = PlayerMap.FindAction("AltAttack", true);
			Jump = PlayerMap.FindAction("Jump", true);
			Sprint = PlayerMap.FindAction("Sprint", true);
			Crouch = PlayerMap.FindAction("Crouch", true);
			Interact = PlayerMap.FindAction("Interact", true);
			Drop = PlayerMap.FindAction("Drop", true);
			Rotate = PlayerMap.FindAction("Rotate", true);
			HotbarScroll = PlayerMap.FindAction("HotbarScroll", true);
			Emotes       = PlayerMap.FindAction("Emotes", false);

			HotbarSlots = new InputAction[8];
			for (int i = 0; i < 8; i++)
			{
				HotbarSlots[i] = PlayerMap.FindAction("Hotbar" + (i + 1), true);
			}

			Close = InventoryMap.FindAction("Close", true);
			TakeAll = InventoryMap.FindAction("TakeAll", true);
			Point = InventoryMap.FindAction("Point", true);
			LeftClick = InventoryMap.FindAction("LeftClick", true);
			RightClick = InventoryMap.FindAction("RightClick", true);

			PlayerMap.Enable();
			InventoryMap.Disable();
		}

		private void OnDestroy()
		{
			if (Asset != null)
			{
				Asset.Disable();
				Destroy(Asset);
				Asset = null;
			}
		}
	}
}
