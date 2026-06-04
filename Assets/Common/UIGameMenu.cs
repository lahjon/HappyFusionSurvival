using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Starter.Common.Menu;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Starter
{
	/// <summary>
	/// Shows in-game menu, handles player connecting/disconnecting to the network game and cursor locking.
	///
	/// The pause menu is the root screen of the global <see cref="MenuManager"/> stack: it is the only thing
	/// that opens when Escape is pressed with nothing else open. It no longer reads the keyboard itself —
	/// Escape is owned entirely by <see cref="MenuManager"/>, and Enter does nothing.
	/// </summary>
	public class UIGameMenu : MonoBehaviour, IMenuScreen
	{
		[Header("Start Game Setup")]
		[Tooltip("Specifies which game mode player should join - e.g. Platformer, ThirdPersonCharacter")]
		public string GameModeIdentifier;
		public NetworkRunner RunnerPrefab;
		public int MaxPlayerCount = 8;

		[Header("Debug")]
		[Tooltip("For debug purposes it is possible to force single-player game (starts faster)")]
		public bool ForceSinglePlayer;
		[Tooltip("Auto-invoke StartGame() on scene load so you don't have to click Start every iteration. Only fires once per app session, so explicit Disconnect still works. Ignored when the game scene was entered from the menu/lobby (a persistent NetworkLauncher already owns the connection) — it only kicks in when the game scene is loaded directly with no launcher (dev press-Play).")]
		public bool AutoStart = true;

		[Header("UI Setup")]
		public CanvasGroup PanelGroup;
		public TMP_InputField RoomText;
		public TMP_InputField NicknameText;
		public TextMeshProUGUI StatusText;
		public GameObject StartGroup;
		public GameObject DisconnectGroup;

		private NetworkRunner _runnerInstance;
		private static string _shutdownStatus;
		private static bool _autoStartConsumed;

		// The MenuManager we subscribed our OpenPauseRequested handler to. Stored so we can unsubscribe even
		// if Instance changes, and to detect whether we have wired up yet (subscription is done lazily in
		// Update because the manager bootstraps AfterSceneLoad and may not exist during our OnEnable).
		private MenuManager _menu;

		/// <summary>Whether a session is live — either a runner this menu created itself (direct press-Play of the
		/// game scene, no launcher) or the persistent <see cref="NetworkLauncher"/> that owns the connection when the
		/// scene was entered from the menu/lobby.</summary>
		private bool Connected => _runnerInstance != null || NetworkLauncher.IsConnected;

		/// <summary>True when this menu owns the runner it created (legacy single-scene / direct press-Play path),
		/// rather than deferring to the persistent launcher.</summary>
		private bool OwnsRunner => _runnerInstance != null;

		string IMenuScreen.MenuName => "Pause";
		bool IMenuScreen.DismissOnEscape => true;
		void IMenuScreen.CloseFromMenu() => HidePause();

		/// <summary>
		/// Editor-only one-shot override set by the "Play Single Player" toolbar button
		/// (see <c>SinglePlayerToolbarButton</c>). It lets that button force isolated-host mode for a single
		/// Play session via the editor's SessionState — without writing the serialized <see cref="ForceSinglePlayer"/>
		/// field, so the scene is never dirtied. The button clears it again when Play mode exits, so the normal
		/// Play button is unaffected. Keep the key string in sync with the toolbar script.
		/// </summary>
#if UNITY_EDITOR
		private static bool EditorSinglePlayerOverride =>
			UnityEditor.SessionState.GetBool("HFS.ForceSinglePlayer", false);
#else
		private const bool EditorSinglePlayerOverride = false;
#endif

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			// Enter Play Mode Options disables domain reload, so statics survive between Play sessions.
			_shutdownStatus = null;
			_autoStartConsumed = false;
		}

		public async void StartGame()
		{
			await Disconnect();

			PlayerPrefs.SetString("PlayerName", NicknameText.text);

			_runnerInstance = Instantiate(RunnerPrefab);

			// Add listener for shutdowns so we can handle unexpected shutdowns
			var events = _runnerInstance.GetComponent<NetworkEvents>();
			events.OnShutdown.AddListener(OnShutdown);

			// Same animated loading overlay used by the menu/lobby flow, so the direct press-Play path is covered too.
			LoadingScreen.BindRunner(_runnerInstance);

			var sceneInfo = new NetworkSceneInfo();
			sceneInfo.AddSceneRef(SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex));

			// Debug isolation: instead of GameMode.Single (which fails to start in this project), spin up a
			// real Host on the cloud so every networked system behaves exactly as in normal multiplayer — but
			// make it impossible for anyone else to land in it:
			//   * random, unguessable session name (no one can target it by name),
			//   * IsVisible = false (hidden from the lobby list, so AutoHostOrClient matchmaking never finds it),
			//   * IsOpen = false (closed to joins even if someone learned the name).
			var isolatedHost = Application.isEditor && (ForceSinglePlayer || EditorSinglePlayerOverride);

			var startArguments = new StartGameArgs()
			{
				GameMode = isolatedHost ? GameMode.Host : GameMode.AutoHostOrClient,
				SessionName = isolatedHost ? "solo-" + System.Guid.NewGuid().ToString("N") : RoomText.text,
				PlayerCount = MaxPlayerCount,
				IsOpen = !isolatedHost,
				IsVisible = !isolatedHost,
				// We need to specify a session property for matchmaking to decide where the player wants to join.
				// Otherwise players from Platformer scene could connect to ThirdPersonCharacter game etc.
				SessionProperties = new Dictionary<string, SessionProperty> {["GameMode"] = GameModeIdentifier},
				Scene = sceneInfo,
			};

			StatusText.text = isolatedHost ? "Starting isolated host..." : "Connecting...";

			var startTask = _runnerInstance.StartGame(startArguments);
			await startTask;

			if (startTask.Result.Ok)
			{
				StatusText.text = "";
				HidePause();
			}
			else
			{
				StatusText.text = $"Connection Failed: {startTask.Result.ShutdownReason}";
			}
		}

		public async void DisconnectClicked()
		{
			if (OwnsRunner)
			{
				await Disconnect();
				return;
			}
			// Connection is owned by the persistent launcher: it shuts the runner down and returns to the main menu.
			NetworkLauncher.Instance?.Disconnect();
		}

		public async void BackToMenu()
		{
			if (OwnsRunner)
			{
				await Disconnect();
				SceneManager.LoadScene(0);
				return;
			}
			NetworkLauncher.Instance?.Disconnect();
		}

		public void TogglePanelVisibility()
		{
			if (PanelGroup.gameObject.activeSelf)
			{
				if (!Connected)
					return; // Panel cannot be hidden if the game is not running

				HidePause();
			}
			else
			{
				OpenPause();
			}
		}

		/// <summary>Show the pause panel and register it as the top of the menu stack. Invoked by
		/// <see cref="MenuManager.OpenPauseRequested"/> (Escape with nothing else open) or a UI button.</summary>
		private void OpenPause()
		{
			PanelGroup.gameObject.SetActive(true);
			_menu?.Open(this);
		}

		/// <summary>Hide the pause panel and pop it off the menu stack. The single close path — used by the
		/// X button, Escape (via <see cref="IMenuScreen.CloseFromMenu"/>), and after a successful connect.</summary>
		private void HidePause()
		{
			PanelGroup.gameObject.SetActive(false);
			_menu?.Close(this);
		}

		private void OnEnable()
		{
			Application.targetFrameRate = 60;

			var nickname = PlayerPrefs.GetString("PlayerName");
			if (string.IsNullOrEmpty(nickname))
			{
				nickname = "Player" + Random.Range(10000, 100000);
			}

			NicknameText.text = nickname;

			// Try to load previous shutdown status
			StatusText.text = _shutdownStatus != null ? _shutdownStatus : string.Empty;
			_shutdownStatus = null;

			// Only self-connect when the game scene was loaded directly with no persistent launcher (dev press-Play).
			// In the normal menu → lobby → game flow the launcher already owns the connection, so we skip this.
			if (AutoStart && !_autoStartConsumed && NetworkLauncher.Instance == null)
			{
				_autoStartConsumed = true;
				PanelGroup.gameObject.SetActive(false);
				StartGame();
			}
		}

		private void Update()
		{
			// Escape is owned by MenuManager (it calls OpenPauseRequested / our CloseFromMenu); Enter does
			// nothing. Here we only keep our own panel state fresh and the gameplay cursor lock. Subscription
			// is lazy because MenuManager bootstraps AfterSceneLoad and may not exist during our OnEnable.
			var menu = MenuManager.Instance;
			if (menu != null && _menu == null)
			{
				_menu = menu;
				_menu.OpenPauseRequested += OpenPause;
			}

			if (PanelGroup.gameObject.activeSelf)
			{
				bool connected = Connected;
				StartGroup.SetActive(!connected);
				DisconnectGroup.SetActive(connected);
				RoomText.interactable = !connected;
				NicknameText.interactable = !connected;

				Cursor.lockState = CursorLockMode.None;
				Cursor.visible = true;
			}
			else if (_menu == null || _menu.IsAnyOpen == false)
			{
				// Baseline gameplay cursor lock — only when no other menu/session owns the cursor.
				Cursor.lockState = CursorLockMode.Locked;
				Cursor.visible = false;
			}
			// else: another screen (loot/crafting/console/…) is open and owns the cursor — leave it alone.
		}

		private void OnDisable()
		{
			if (_menu != null)
			{
				_menu.OpenPauseRequested -= OpenPause;
				_menu.Close(this);
				_menu = null;
			}
		}

		public async Task Disconnect()
		{
			if (_runnerInstance == null)
				return;

			StatusText.text = "Disconnecting...";
			PanelGroup.interactable = false;

			// Remove shutdown listener since we are disconnecting deliberately
			var events = _runnerInstance.GetComponent<NetworkEvents>();
			events.OnShutdown.RemoveListener(OnShutdown);

			await _runnerInstance.Shutdown();
			_runnerInstance = null;

			// Reset of scene network objects is needed, reload the whole scene
			SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
		}

		private void OnShutdown(NetworkRunner runner, ShutdownReason reason)
		{
			// Unexpected shutdown happened (e.g. Host disconnected)

			// Save status into static variable, it will be used in OnEnable after scene load
			_shutdownStatus = $"Shutdown: {reason}";
			Debug.LogWarning(_shutdownStatus);

			// Reset of scene network objects is needed, reload the whole scene
			SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
		}
	}
}
