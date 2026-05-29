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

		/// <summary>
		/// Frame number on which any local <see cref="InputContextController"/> last switched back to
		/// Player mode (i.e. a session closed). <see cref="UIGameMenu"/> polls this so that the same
		/// Escape press that closed a session menu doesn't also pop the pause menu in the same frame —
		/// the Close input action and the menu's raw <c>Keyboard.escapeKey.wasPressedThisFrame</c> both
		/// fire on that press.
		/// </summary>
		public static int LastExitInventoryFrame { get; private set; } = -1;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			LastExitInventoryFrame = -1;
		}

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

			LastExitInventoryFrame = Time.frameCount;
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
