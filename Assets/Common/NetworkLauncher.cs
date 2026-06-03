using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Starter
{
	/// <summary>
	/// Owns the single <see cref="NetworkRunner"/> for the whole session lifetime — across the local main menu, the
	/// networked lobby scene, and the game scene. It is the one object that decides Host vs. Join and that survives
	/// every scene transition (it is <c>DontDestroyOnLoad</c> and a process-wide singleton). Per-scene UI never
	/// instantiates a runner itself; it calls <see cref="HostGame"/> / <see cref="JoinGame"/> / <see cref="Disconnect"/>
	/// here and reads <see cref="IsConnected"/>.
	///
	/// Scene flow it drives (the main menu scene doubles as the networked lobby — there is no separate lobby scene):
	///   00_MainMenu (this lives here) → Host/Join starts the runner with 00_MainMenu itself as the network scene
	///   (host via <see cref="StartGameArgs.Scene"/>, clients follow the host automatically), so the scene-placed
	///   <see cref="Shooter.LobbyManager"/> spawns and the lobby panel appears → <see cref="Shooter.LobbyManager.StartMatch"/>
	///   loads the game scene → <see cref="Shooter.MatchManager"/> loads 00_MainMenu again on MatchOver (back to the
	///   lobby). A full <see cref="Disconnect"/> shuts the runner down and reloads the menu disconnected.
	///
	/// MonoBehaviour by design: connection lifecycle / cursor-free menu input is local. All replicated lobby/match
	/// state lives on the networked managers in their respective scenes.
	/// </summary>
	public sealed class NetworkLauncher : MonoBehaviour
	{
		public static NetworkLauncher Instance { get; private set; }

		[Header("Connection")]
		public NetworkRunner RunnerPrefab;
		[Tooltip("Matchmaking partition so this mode never shares rooms with other Starter Kit modes.")]
		public string GameModeIdentifier = "Shooter";
		[Tooltip("Up to 18 for a full match.")]
		public int MaxPlayerCount = 18;

		[Header("Scene build indices")]
		[Tooltip("Build index of the main menu scene. It doubles as the networked lobby and is the scene returned to on disconnect / match-over.")]
		public int MainMenuSceneIndex = 0;

		private NetworkRunner _runner;

		/// <summary>The active runner, or null when disconnected. Read by per-scene UI (e.g. pause menu) that does not own it.</summary>
		public static NetworkRunner ActiveRunner => Instance != null ? Instance._runner : null;
		public static bool IsConnected => ActiveRunner != null && ActiveRunner.IsRunning;

		/// <summary>True while a Host/Join attempt is in flight (from the click until the session is up or fails). The
		/// menu's busy overlay reads this to lock interaction so a second click can't spin up a duplicate runner.</summary>
		public bool IsBusy { get; private set; }

		/// <summary>Static convenience for UI — a connect is currently in progress.</summary>
		public static bool IsConnecting => Instance != null && Instance.IsBusy;

		/// <summary>Last status line ("Hosting…", "Failed: …"). Polled by the main-menu UI to show connection feedback.</summary>
		public string Status { get; private set; } = string.Empty;

		private void Awake()
		{
			// Process-wide singleton. The launcher is placed in the main menu scene; when we return there after a
			// disconnect a second copy loads with the scene and immediately self-destructs, leaving the original.
			if (Instance != null && Instance != this)
			{
				Destroy(gameObject);
				return;
			}
			Instance = this;
			DontDestroyOnLoad(gameObject);
		}

		private void OnDestroy()
		{
			if (Instance == this) Instance = null;
		}

		// =========================================================================
		// Public entry points (button-friendly, zero-return)
		// =========================================================================

		/// <summary>Start a brand-new session as the Host. The menu scene becomes the networked lobby. No-op if already connected.</summary>
		public async void HostGame(string roomName, string nickname)
		{
			await Connect(GameMode.Host, roomName, nickname, setInitialScene: true);
		}

		/// <summary>Join an existing host's session as a Client. The host dictates the scene, so we follow it (no scene arg).</summary>
		public async void JoinGame(string roomName, string nickname)
		{
			await Connect(GameMode.Client, roomName, nickname, setInitialScene: false);
		}

		private async Task Connect(GameMode mode, string roomName, string nickname, bool setInitialScene)
		{
			// Re-entrancy guard — without it, spamming Host/Join fires several StartGame calls before the first
			// resolves (IsConnected only flips once connected), each instantiating a runner and breaking the session.
			if (IsConnected || IsBusy)
				return;
			IsBusy = true;

			try
			{
				if (string.IsNullOrWhiteSpace(nickname) == false)
					PlayerPrefs.SetString("PlayerName", nickname.Trim());

				SetStatus(mode == GameMode.Host ? "Hosting…" : "Joining…");

				_runner = Instantiate(RunnerPrefab);

				// The host sets the menu scene as the network scene, which reloads it (Single mode). Keep the runner
				// alive across that reload so the session survives — Fusion normally does this, but make it explicit.
				DontDestroyOnLoad(_runner.gameObject);

				// React to unexpected shutdowns (e.g. host left) by bouncing back to the main menu.
				var events = _runner.GetComponent<NetworkEvents>();
				if (events != null)
					events.OnShutdown.AddListener(OnRunnerShutdown);

				// Cover every networked scene transition (lobby → game, game → lobby) with the animated loading
				// overlay, on host and clients alike. It shows on OnSceneLoadStart and hides when the scene is ready.
				LoadingScreen.BindRunner(_runner);

				var args = new StartGameArgs
				{
					GameMode = mode,
					// Empty room name → Fusion matchmaking (host opens a random session; client joins any open one
					// with the matching GameMode property). A typed name lets friends rendezvous on the same room.
					SessionName = string.IsNullOrWhiteSpace(roomName) ? null : roomName.Trim(),
					PlayerCount = MaxPlayerCount,
					SessionProperties = new Dictionary<string, SessionProperty> { ["GameMode"] = GameModeIdentifier },
				};

				// Only the host specifies the initial scene — the menu scene itself, so its LobbyManager spawns and
				// the lobby goes live. Clients are moved to whatever scene the host currently has (menu/lobby, then game).
				if (setInitialScene)
				{
					var sceneInfo = new NetworkSceneInfo();
					sceneInfo.AddSceneRef(SceneRef.FromIndex(MainMenuSceneIndex));
					args.Scene = sceneInfo;
				}

				var result = await _runner.StartGame(args);

				if (result.Ok)
				{
					SetStatus(string.Empty);
				}
				else
				{
					SetStatus($"Connection failed: {result.ShutdownReason}");
					CleanupRunner();
				}
			}
			finally
			{
				IsBusy = false;
			}
		}

		/// <summary>Deliberate disconnect: shut the runner down and return to the local main menu.</summary>
		public async void Disconnect()
		{
			if (_runner != null)
			{
				var events = _runner.GetComponent<NetworkEvents>();
				if (events != null)
					events.OnShutdown.RemoveListener(OnRunnerShutdown);

				await _runner.Shutdown();
			}

			CleanupRunner();
			SceneManager.LoadScene(MainMenuSceneIndex);
		}

		private void OnRunnerShutdown(NetworkRunner runner, ShutdownReason reason)
		{
			SetStatus($"Disconnected: {reason}");
			Debug.LogWarning($"[NetworkLauncher] Runner shutdown: {reason}");
			CleanupRunner();

			// Avoid stacking a reload if we are already on the menu scene.
			if (SceneManager.GetActiveScene().buildIndex != MainMenuSceneIndex)
				SceneManager.LoadScene(MainMenuSceneIndex);
		}

		private void CleanupRunner()
		{
			_runner = null;
		}

		private void SetStatus(string status)
		{
			Status = status;
		}
	}
}
