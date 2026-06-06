using System.Collections.Generic;
using Starter.Common.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.MainMenu
{
	/// <summary>
	/// Stack-based navigation for the local main-menu panels (Main → Connect / Options …). Pushing a panel hides the
	/// current one and shows the new; popping reverses it. Escape goes back one level — routed through the global
	/// <see cref="MenuManager"/> (the single owner of Escape; this never reads the keyboard itself) via its
	/// <see cref="MenuManager.OpenPauseRequested"/> hook, which fires on Escape when nothing else is open (the menu
	/// scene has no pause menu, so it's free to repurpose for "back"). The Back button is shown only while there is
	/// something on the stack.
	///
	/// While a networked lobby is live (<see cref="Shooter.LobbyManager.Instance"/> != null) the whole local menu
	/// yields — every panel and the Back button hide so the lobby panel (<see cref="Shooter.UILobby"/>) shows in their
	/// place. Leaving the lobby (which reloads the menu scene) returns navigation to the root.
	/// </summary>
	public sealed class MainMenuNav : MonoBehaviour
	{
		[Header("Panels")]
		[Tooltip("Root panel shown at the base of the stack (Start Game / Options / Exit).")]
		public GameObject MainPanel;
		[Tooltip("Connect panel (Host / Join). Pushed by Start Game.")]
		public GameObject ConnectPanel;
		[Tooltip("Optional options panel. Pushed by Options; the button is a no-op until this is assigned.")]
		public GameObject OptionsPanel;

		[Header("Back")]
		[Tooltip("Shown only while something is on the stack (i.e. not on the root panel). Should live OUTSIDE the panels so it stays visible over a pushed panel.")]
		public GameObject BackButton;

		private readonly List<GameObject> _stack = new();
		private MenuManager _menu;

		// =========================================================================
		// Button entry points (button-friendly, zero-arg)
		// =========================================================================

		/// <summary>Start Game → show the Connect panel.</summary>
		public void OpenConnect() => Push(ConnectPanel);

		/// <summary>Options → show the Options panel (no-op if none is assigned).</summary>
		public void OpenOptions() => Push(OptionsPanel);

		/// <summary>Back button / Escape → go back one level.</summary>
		public void Back() => Pop();

		/// <summary>Exit Game → quit the application.</summary>
		public void ExitGame()
		{
			Application.Quit();
			#if UNITY_EDITOR
				UnityEditor.EditorApplication.ExitPlaymode();
			#endif
		}

		// =========================================================================
		// Stack
		// =========================================================================

		private void Push(GameObject panel)
		{
			if (panel == null) return;
			Current().SetActive(false);
			panel.SetActive(true);
			_stack.Add(panel);
		}

		private void Pop()
		{
			if (_stack.Count == 0) return;
			var top = _stack[_stack.Count - 1];
			if (top != null) top.SetActive(false);
			_stack.RemoveAt(_stack.Count - 1);
			Current().SetActive(true);
		}

		private GameObject Current() => _stack.Count > 0 ? _stack[_stack.Count - 1] : MainPanel;

		// =========================================================================
		// Lifecycle
		// =========================================================================

		private void OnEnable() => ResetToRoot();

		private void ResetToRoot()
		{
			_stack.Clear();
			if (ConnectPanel != null) ConnectPanel.SetActive(false);
			if (OptionsPanel != null) OptionsPanel.SetActive(false);
			if (MainPanel != null) MainPanel.SetActive(true);
		}

		private void Update()
		{
			// Lazy-bind to the global menu manager (it bootstraps AfterSceneLoad).
			if (_menu == null)
			{
				var mm = MenuManager.Instance;
				if (mm != null)
				{
					_menu = mm;
					_menu.OpenPauseRequested += OnEscape;
				}
			}

			// While in a networked lobby, hide the whole local menu — the lobby panel takes over.
			bool inLobby = Shooter.LobbyManager.Instance != null;
			if (inLobby)
			{
				SetActiveIfNeeded(MainPanel, false);
				SetActiveIfNeeded(ConnectPanel, false);
				SetActiveIfNeeded(OptionsPanel, false);
				SetActiveIfNeeded(BackButton, false);
				return;
			}

			// Not in a lobby: make sure the current panel is up (e.g. after leaving a lobby), and show the Back
			// button only when there is somewhere to go back to.
			SetActiveIfNeeded(Current(), true);
			SetActiveIfNeeded(BackButton, _stack.Count > 0);
		}

		private static void SetActiveIfNeeded(GameObject go, bool active)
		{
			if (go != null && go.activeSelf != active) go.SetActive(active);
		}

		private void OnEscape()
		{
			// Escape is a no-op while the lobby owns the screen; otherwise it walks the stack back one level.
			if (Shooter.LobbyManager.Instance != null) return;
			Pop();
		}

		private void OnDisable()
		{
			if (_menu != null)
			{
				_menu.OpenPauseRequested -= OnEscape;
				_menu = null;
			}
		}
	}
}
