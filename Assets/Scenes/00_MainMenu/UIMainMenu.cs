using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Starter.MainMenu
{
	/// <summary>
	/// Local front-screen controller for 00_MainMenu. Hosts/joins a networked session through the persistent
	/// <see cref="NetworkLauncher"/> (which loads the lobby scene and survives every scene transition), and offers a
	/// plain scene-load / quit for non-networked entry. No networking lives here — the buttons just gather a nickname
	/// and room name and hand them to the launcher.
	/// </summary>
	public class UIMainMenu : MonoBehaviour
	{
		[Header("Connection inputs (optional)")]
		[Tooltip("Player nickname. Persisted to PlayerPrefs[\"PlayerName\"], shared with the in-game nameplate.")]
		public TMP_InputField NicknameField;
		[Tooltip("Room name to host/join. Leave blank for quick matchmaking (host opens a random room, join finds one).")]
		public TMP_InputField RoomField;
		[Tooltip("Optional status line mirroring the launcher (Hosting… / Joining… / Failed…).")]
		public TMP_Text StatusText;

		// =========================================================================
		// Connection
		// =========================================================================

		/// <summary>Host button: start a new session and load the lobby scene.</summary>
		public void HostClicked()
		{
			if (NetworkLauncher.Instance == null) return;
			NetworkLauncher.Instance.HostGame(RoomText, NicknameText);
		}

		/// <summary>Join button: connect to an existing host's session (and follow it into the lobby scene).</summary>
		public void JoinClicked()
		{
			if (NetworkLauncher.Instance == null) return;
			NetworkLauncher.Instance.JoinGame(RoomText, NicknameText);
		}

		private string RoomText => RoomField != null ? RoomField.text : string.Empty;
		private string NicknameText => NicknameField != null ? NicknameField.text : string.Empty;

		// =========================================================================
		// Non-networked entry / quit (kept for compatibility)
		// =========================================================================

		public void LoadScene(int index)
		{
			SceneManager.LoadScene(index);
		}

		public void QuitGame()
		{
			Application.Quit();

			#if UNITY_EDITOR
				UnityEditor.EditorApplication.ExitPlaymode();
			#endif
		}

		private void OnEnable()
		{
			// Ensure the cursor is visible when coming back from the game
			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = true;

			// Pre-fill the nickname from the last session (or a random default).
			if (NicknameField != null)
			{
				var nickname = PlayerPrefs.GetString("PlayerName");
				if (string.IsNullOrEmpty(nickname))
					nickname = "Player" + Random.Range(10000, 100000);
				NicknameField.text = nickname;
			}
		}

		private void Update()
		{
			// Panel visibility (Main / Connect / lobby) is owned by MainMenuNav + UILobby; here we only surface the
			// launcher's connection status.
			if (StatusText != null && NetworkLauncher.Instance != null)
				StatusText.text = NetworkLauncher.Instance.Status;
		}
	}
}
