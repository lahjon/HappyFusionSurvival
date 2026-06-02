using System.Collections.Generic;
using System.Text;
using Starter.Common.Menu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only lobby front end for a match. Lets the host pick team size (Solo / Duo / Trio) and start the
	/// round, and shows every connected player grouped by the team they will land on. Reads networked state from
	/// <see cref="MatchManager"/> / <see cref="TeamManager"/> and mutates only through their host-guarded methods.
	///
	/// The host holds state authority over the scene managers, so its direct <see cref="TeamManager.SetTeamSize"/>
	/// / <see cref="MatchManager.BeginMatch"/> calls take effect; on clients those calls are no-ops, so the UI is
	/// gated to host-only interactables and everyone else sees a read-only roster preview.
	///
	/// This component MUST sit on an always-active object and toggle a separate <see cref="Panel"/> child — an
	/// inactive GameObject stops running Update, so it could never notice the phase returning to Lobby.
	///
	/// MonoBehaviour by design — pure view/input. Authoritative state lives on the networked managers.
	/// </summary>
	public sealed class UIMatchLobby : MonoBehaviour, IMenuScreen
	{
		// Modal screen: while the lobby shows, Escape is swallowed (the lobby is phase-driven and closes itself
		// when the match begins), not passed to the pause menu.
		string IMenuScreen.MenuName => "MatchLobby";
		bool IMenuScreen.DismissOnEscape => false;
		void IMenuScreen.CloseFromMenu() { }

		private bool _menuRegistered;

		/// <summary>True on this peer while the lobby panel is showing (Phase == Lobby). <see cref="UIGameMenu"/>
		/// reads this to yield cursor/Escape control, the same contract used by the loot/crafting/etc. sessions —
		/// otherwise UIGameMenu would re-lock the cursor every frame and the lobby buttons would be unclickable.</summary>
		public static bool IsLobbyOpen { get; private set; }

		[Header("Root")]
		[Tooltip("Shown only while MatchManager.Phase == Lobby; hidden in every other phase. Must be a CHILD of this component's object, not the object itself.")]
		public GameObject Panel;

		[Header("Host controls")]
		[Tooltip("Team-size buttons. Index 0 = Solo (1), 1 = Duo (2), 2 = Trio (3). Order matters.")]
		public Button SoloButton;
		public Button DuoButton;
		public Button TrioButton;
		public Button BeginButton;

		[Header("Display")]
		[Tooltip("Live preview of the roster grouped into teams by the selected size.")]
		public TextMeshProUGUI TeamsText;
		[Tooltip("Optional message shown to non-host players in place of the host controls.")]
		public GameObject ClientHint;

		[Header("Selection tint")]
		public Color SelectedColor = new Color(0.30f, 0.75f, 1f, 1f);
		public Color UnselectedColor = Color.white;

		[Tooltip("Seconds between roster refreshes. The roster scan is cheap but need not run every frame.")]
		[Min(0.05f)] public float RefreshInterval = 0.5f;

		private readonly List<Player> _roster = new(TeamManager.MaxPlayers);
		private readonly StringBuilder _sb = new(256);
		private float _nextRefresh;

		private void OnEnable()
		{
			Wire(SoloButton, 1);
			Wire(DuoButton, 2);
			Wire(TrioButton, 3);
			if (BeginButton != null) BeginButton.onClick.AddListener(OnBeginClicked);
		}

		private void OnDisable()
		{
			IsLobbyOpen = false;
			if (_menuRegistered)
			{
				MenuManager.Instance?.Close(this);
				_menuRegistered = false;
			}
			if (SoloButton != null) SoloButton.onClick.RemoveAllListeners();
			if (DuoButton != null) DuoButton.onClick.RemoveAllListeners();
			if (TrioButton != null) TrioButton.onClick.RemoveAllListeners();
			if (BeginButton != null) BeginButton.onClick.RemoveListener(OnBeginClicked);
		}

		private void Wire(Button button, int size)
		{
			if (button == null) return;
			button.onClick.AddListener(() => TeamManager.Instance?.SetTeamSize(size));
		}

		private bool IsHost
		{
			get
			{
				var m = MatchManager.Instance;
				return m != null && m.Object != null && m.Object.HasStateAuthority;
			}
		}

		private void Update()
		{
			var match = MatchManager.Instance;
			bool inLobby = match != null && match.Phase == MatchPhase.Lobby;

			if (Panel != null && Panel.activeSelf != inLobby)
				Panel.SetActive(inLobby);

			IsLobbyOpen = inLobby;

			// Register/unregister on the menu stack as the lobby opens/closes. Retries Open until the manager
			// exists (it bootstraps AfterSceneLoad), so a lobby that is already up on the first frame still lands.
			if (inLobby)
			{
				if (_menuRegistered == false && MenuManager.Instance != null)
				{
					MenuManager.Instance.Open(this);
					_menuRegistered = true;
				}
			}
			else if (_menuRegistered)
			{
				MenuManager.Instance?.Close(this);
				_menuRegistered = false;
			}

			if (inLobby == false) return;

			// The lobby is a modal pre-game screen — free the cursor so the buttons are clickable. UIGameMenu
			// yields here because IsLobbyOpen is set (see its Update guard).
			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = true;

			bool host = IsHost;
			var team = TeamManager.Instance;
			int size = team != null ? Mathf.Clamp(team.TeamSize, TeamManager.MinTeamSize, TeamManager.MaxTeamSize) : 0;

			// Host-only interactables; clients see a read-only preview.
			SetButton(SoloButton, host, size == 1);
			SetButton(DuoButton, host, size == 2);
			SetButton(TrioButton, host, size == 3);
			if (BeginButton != null)
			{
				BeginButton.gameObject.SetActive(host);
				BeginButton.interactable = host && _roster.Count > 0;
			}
			if (ClientHint != null) ClientHint.SetActive(host == false);

			if (Time.time >= _nextRefresh)
			{
				_nextRefresh = Time.time + RefreshInterval;
				RefreshTeams();
			}
		}

		private void SetButton(Button button, bool host, bool selected)
		{
			if (button == null) return;
			button.interactable = host;
			// Tint via the Image directly rather than Button.targetGraphic — the latter isn't auto-wired when
			// the Button is added by code, but the Image is always the raycast target so clicks work regardless.
			var img = button.GetComponent<Image>();
			if (img != null) img.color = selected ? SelectedColor : UnselectedColor;
		}

		private void OnBeginClicked()
		{
			// Host-only entry point; MatchManager guards on state authority + Lobby phase internally.
			MatchManager.Instance?.BeginMatch();
		}

		/// <summary>
		/// Builds the team preview. During Lobby the teams are not yet committed (assignment happens at
		/// BeginMatch), so we mirror <see cref="TeamManager.AssignTeams"/>' deterministic rule here — all spawned
		/// players sorted by PlayerId, chunked by the selected size — to show exactly what BeginMatch will produce.
		/// Players are networked objects so the roster is identical on every peer.
		/// </summary>
		private void RefreshTeams()
		{
			var team = TeamManager.Instance;
			int size = team != null ? Mathf.Clamp(team.TeamSize, TeamManager.MinTeamSize, TeamManager.MaxTeamSize) : 1;

			_roster.Clear();
			foreach (var p in FindObjectsByType<Player>(FindObjectsSortMode.None))
			{
				if (p != null && p.Object != null) _roster.Add(p);
			}
			_roster.Sort((a, b) => a.Object.InputAuthority.PlayerId.CompareTo(b.Object.InputAuthority.PlayerId));

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

				var p = _roster[i];
				var nick = p.Nickname;
				_sb.Append(string.IsNullOrEmpty(nick) ? "Player" + p.Object.InputAuthority.PlayerId : nick.ToString());
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
