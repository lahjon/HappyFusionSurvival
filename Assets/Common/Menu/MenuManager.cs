using System;
using System.Collections.Generic;
using QFSW.QC;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Starter.Common.Menu
{
	/// <summary>
	/// Local-only singleton that owns the in-game menu stack and is the SOLE reader of the Escape key.
	/// Every openable UI (pause menu, loot / crafting / quest / shop / computer sessions, sleep, match
	/// lobby/result) registers itself as an <see cref="IMenuScreen"/> via <see cref="Open"/> / <see cref="Close"/>.
	///
	/// Escape resolution order:
	///   1. If the Quantum Console is active, close it first (it floats above everything and opens by its own key).
	///   2. Else if any screen is open, the top screen handles Escape — closing if <see cref="IMenuScreen.DismissOnEscape"/>,
	///      otherwise swallowing it (modal).
	///   3. Else raise <see cref="OpenPauseRequested"/> so the pause menu opens.
	///
	/// Enter is intentionally never read here — opening the pause menu is Escape-only.
	///
	/// Pure local view/input, so a plain MonoBehaviour. Auto-bootstrapped (no scene/prefab wiring) to avoid
	/// touching networked prefabs.
	/// </summary>
	public sealed class MenuManager : MonoBehaviour
	{
		public static MenuManager Instance { get; private set; }

		/// <summary>Raised when Escape is pressed with nothing open. The pause menu subscribes and shows itself.</summary>
		public event Action OpenPauseRequested;

		private readonly List<IMenuScreen> _stack = new();
		private QuantumConsole _console;

		/// <summary>True while at least one menu screen is open. Used by cursor-lock baselines to yield control.</summary>
		public bool IsAnyOpen => _stack.Count > 0;

		/// <summary>The top-most open screen, or null if nothing is open.</summary>
		public IMenuScreen Top => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Bootstrap()
		{
			if (Instance != null)
				return;

			var go = new GameObject("MenuManager");
			DontDestroyOnLoad(go);
			Instance = go.AddComponent<MenuManager>();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			// Enter Play Mode Options can disable domain reload, so statics survive between Play sessions.
			Instance = null;
		}

		private void OnDestroy()
		{
			if (Instance == this)
				Instance = null;
		}

		/// <summary>Push a screen onto the stack (idempotent — re-opening a screen already on top is a no-op).</summary>
		public void Open(IMenuScreen screen)
		{
			if (screen == null || _stack.Contains(screen))
				return;

			_stack.Add(screen);
		}

		/// <summary>Remove a screen from anywhere in the stack (safe for out-of-order / auto-close).</summary>
		public void Close(IMenuScreen screen)
		{
			if (screen == null)
				return;

			_stack.Remove(screen);
		}

		private void Update()
		{
			var keyboard = Keyboard.current;
			if (keyboard == null || keyboard.escapeKey.wasPressedThisFrame == false)
				return;

			// 1. Quantum Console floats above the whole stack and opens via its own key — close it first.
			var console = ResolveConsole();
			if (console != null && console.IsActive)
			{
				console.Deactivate();
				return;
			}

			// 2. The top screen owns Escape. Modal screens swallow it without closing.
			var top = Top;
			if (top != null)
			{
				if (top.DismissOnEscape)
					top.CloseFromMenu();
				return;
			}

			// 3. Nothing open — open the pause menu.
			OpenPauseRequested?.Invoke();
		}

		// QuantumConsole.Instance is only set when its singleton option is enabled; fall back to a cached scene lookup.
		private QuantumConsole ResolveConsole()
		{
			if (QuantumConsole.Instance != null)
				return QuantumConsole.Instance;

			if (_console == null)
				_console = FindAnyObjectByType<QuantumConsole>(FindObjectsInactive.Include);

			return _console;
		}
	}
}
