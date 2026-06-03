using System.Collections.Generic;
using System.Text;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.Shooter
{
	/// <summary>
	/// Local front end for the pre-match lobby scene (01_Lobby). Shows every connected player grouped into the teams
	/// they will land on, lets the host pick team size (Solo / Duo / Trio) and start the match, and lets anyone leave
	/// back to the main menu. Reads networked state from <see cref="LobbyManager"/> and mutates only through its
	/// host-guarded methods (client clicks are no-ops on the managers, so the UI is gated to host-only interactables).
	///
	/// Unlike the in-game <see cref="UIMatchLobby"/>, the roster comes from <see cref="NetworkRunner.ActivePlayers"/>
	/// (+ the <see cref="LobbyManager.Names"/> registry), because no <see cref="Player"/> avatars exist in this scene.
	///
	/// MonoBehaviour by design — pure view/input. Authoritative state lives on <see cref="LobbyManager"/>.
	/// </summary>
	public sealed class UILobby : MonoBehaviour
	{
		[Header("Root")]
		[Tooltip("Shown only while connected and the networked LobbyManager has spawned (i.e. in the lobby). Hidden when disconnected so the main-menu connect panel shows. Must be a CHILD of this component's object, not the object itself.")]
		public GameObject Panel;

		[Header("Host controls")]
		[Tooltip("Team-size buttons. Solo (1) / Duo (2) / Trio (3).")]
		public Button SoloButton;
		public Button DuoButton;
		public Button TrioButton;
		[Tooltip("Host-only. Loads the game scene for everyone.")]
		public Button StartButton;

		[Header("Always available")]
		[Tooltip("Disconnects and returns to the main menu.")]
		public Button LeaveButton;

		[Header("Display")]
		[Tooltip("Live preview of the roster grouped into teams by the selected size.")]
		public TextMeshProUGUI TeamsText;
		[Tooltip("Shown to non-host players in place of the host controls.")]
		public GameObject ClientHint;
		[Tooltip("Optional status line (connection / waiting).")]
		public TextMeshProUGUI StatusText;

		[Header("Selection tint")]
		public Color SelectedColor = new Color(0.30f, 0.75f, 1f, 1f);
		public Color UnselectedColor = Color.white;

		[Min(0.05f)] public float RefreshInterval = 0.5f;

		private readonly List<PlayerRef> _roster = new(LobbyManager.MaxPlayers);
		private readonly StringBuilder _sb = new(256);
		private float _nextRefresh;

		private void OnEnable()
		{
			Wire(SoloButton, 1);
			Wire(DuoButton, 2);
			Wire(TrioButton, 3);
			if (StartButton != null) StartButton.onClick.AddListener(OnStartClicked);
			if (LeaveButton != null) LeaveButton.onClick.AddListener(OnLeaveClicked);
		}

		private void OnDisable()
		{
			if (SoloButton != null) SoloButton.onClick.RemoveAllListeners();
			if (DuoButton != null) DuoButton.onClick.RemoveAllListeners();
			if (TrioButton != null) TrioButton.onClick.RemoveAllListeners();
			if (StartButton != null) StartButton.onClick.RemoveListener(OnStartClicked);
			if (LeaveButton != null) LeaveButton.onClick.RemoveListener(OnLeaveClicked);
		}

		private void Wire(Button button, int size)
		{
			if (button == null) return;
			button.onClick.AddListener(() => LobbyManager.Instance?.SetTeamSize(size));
		}

		private void OnStartClicked() => LobbyManager.Instance?.StartMatch();
		private void OnLeaveClicked() => NetworkLauncher.Instance?.Disconnect();

		private bool IsHost
		{
			get
			{
				var l = LobbyManager.Instance;
				return l != null && l.Object != null && l.Object.HasStateAuthority;
			}
		}

		private void Update()
		{
			var lobby = LobbyManager.Instance;
			bool inLobby = lobby != null;

			// The menu scene doubles as the lobby: show the lobby panel only once connected and the networked
			// LobbyManager has spawned. While disconnected it stays hidden so the main-menu connect panel shows.
			if (Panel != null && Panel.activeSelf != inLobby)
				Panel.SetActive(inLobby);

			if (!inLobby)
				return;

			// Cursor-driven menu — keep it free so the buttons are clickable.
			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = true;

			if (StatusText != null) StatusText.text = string.Empty;

			bool host = IsHost;
			int size = Mathf.Clamp(lobby.TeamSize, TeamManager.MinTeamSize, TeamManager.MaxTeamSize);

			SetButton(SoloButton, host, size == 1);
			SetButton(DuoButton, host, size == 2);
			SetButton(TrioButton, host, size == 3);

			if (StartButton != null)
			{
				StartButton.gameObject.SetActive(host);
				StartButton.interactable = host && _roster.Count > 0;
			}
			if (ClientHint != null) ClientHint.SetActive(host == false);

			if (Time.time >= _nextRefresh)
			{
				_nextRefresh = Time.time + RefreshInterval;
				RefreshTeams(lobby, size);
			}
		}

		private void SetButton(Button button, bool host, bool selected)
		{
			if (button == null) return;
			button.interactable = host;
			var img = button.GetComponent<Image>();
			if (img != null) img.color = selected ? SelectedColor : UnselectedColor;
		}

		/// <summary>
		/// Mirrors <see cref="TeamManager.AssignTeams"/>' deterministic rule (all players sorted by PlayerId, chunked
		/// by team size) so the preview shows exactly what the match will produce.
		/// </summary>
		private void RefreshTeams(LobbyManager lobby, int size)
		{
			_roster.Clear();
			var active = lobby.Runner != null ? lobby.Runner.ActivePlayers : null;
			if (active != null)
			{
				foreach (var p in active)
					_roster.Add(p);
			}
			_roster.Sort((a, b) => a.PlayerId.CompareTo(b.PlayerId));

			if (TeamsText == null) return;

			_sb.Clear();
			_sb.Append("Team size: ").Append(SizeLabel(size));

			int currentTeam = -1;
			for (int i = 0; i < _roster.Count; i++)
			{
				int teamId = i / size;
				if (teamId != currentTeam)
				{
					currentTeam = teamId;
					_sb.Append("\n\nTeam ").Append(teamId + 1).Append(": ");
				}
				else
				{
					_sb.Append(", ");
				}
				_sb.Append(lobby.NameOf(_roster[i]));
			}

			if (_roster.Count == 0)
				_sb.Append("\n\n(waiting for players…)");

			TeamsText.text = _sb.ToString();
		}

		private static string SizeLabel(int size) => size switch
		{
			1 => "Solo",
			2 => "Duo",
			3 => "Trio",
			_ => size + "-player",
		};
	}
}
