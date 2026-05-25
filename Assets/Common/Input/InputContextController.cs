using UnityEngine;

namespace Starter.Common.Input
{
	/// <summary>
	/// Local-only helper on the player root. Toggles between the Player and
	/// Inventory action maps and the matching cursor state. Used by LootSession
	/// when opening / closing a container UI.
	/// </summary>
	[RequireComponent(typeof(GameInputActions))]
	public sealed class InputContextController : MonoBehaviour
	{
		public enum Context
		{
			Player,
			Inventory,
		}

		private GameInputActions _actions;
		private Context _current = Context.Player;

		public Context CurrentContext => _current;

		private void Awake()
		{
			_actions = GetComponent<GameInputActions>();
		}

		public void EnterPlayerMode()
		{
			if (_actions == null || _actions.IsInitialized == false)
				return;

			_actions.InventoryMap.Disable();
			_actions.PlayerMap.Enable();

			Cursor.lockState = CursorLockMode.Locked;
			Cursor.visible = false;

			_current = Context.Player;
		}

		public void EnterInventoryMode()
		{
			if (_actions == null || _actions.IsInitialized == false)
				return;

			_actions.PlayerMap.Disable();
			_actions.InventoryMap.Enable();

			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = true;

			_current = Context.Inventory;
		}
	}
}
