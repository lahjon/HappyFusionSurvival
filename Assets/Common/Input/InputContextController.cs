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
		private bool _consoleSuppressed;

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
			_current = Context.Player;

			if (_actions == null || _actions.IsInitialized == false)
				return;

			// While the console is open it owns all input; just remember the desired context —
			// SetConsoleSuppressed(false) re-applies it (re-enabling the map) on close.
			if (_consoleSuppressed)
				return;

			_actions.InventoryMap.Disable();
			_actions.PlayerMap.Enable();

			Cursor.lockState = CursorLockMode.Locked;
			Cursor.visible = false;

			LastExitInventoryFrame = Time.frameCount;
		}

		public void EnterInventoryMode()
		{
			_current = Context.Inventory;

			if (_actions == null || _actions.IsInitialized == false)
				return;

			if (_consoleSuppressed)
				return;

			_actions.PlayerMap.Disable();
			_actions.InventoryMap.Enable();

			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = true;
		}

		/// <summary>
		/// While the Quantum Console is open it owns ALL input: both action maps are disabled so no
		/// gameplay or UI action fires, and the cursor is freed for typing. Escape still closes the
		/// console because <see cref="Menu.MenuManager"/> reads it raw and resolves the console before
		/// anything else. On close the Player/Inventory context that was active beforehand is restored.
		/// </summary>
		public void SetConsoleSuppressed(bool suppressed)
		{
			if (_consoleSuppressed == suppressed)
				return;

			_consoleSuppressed = suppressed;

			if (_actions == null || _actions.IsInitialized == false)
				return;

			if (suppressed)
			{
				_actions.PlayerMap.Disable();
				_actions.InventoryMap.Disable();

				Cursor.lockState = CursorLockMode.None;
				Cursor.visible = true;
			}
			else if (_current == Context.Inventory)
			{
				EnterInventoryMode();
			}
			else
			{
				EnterPlayerMode();
			}
		}
	}
}
