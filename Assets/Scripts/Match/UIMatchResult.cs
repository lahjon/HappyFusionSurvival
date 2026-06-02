using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only end-of-match screen. Shows a full-screen VICTORY / DEFEAT overlay while
	/// <see cref="MatchManager.Phase"/> == <see cref="MatchPhase.MatchOver"/>, reading the networked
	/// <see cref="MatchManager.WinningTeamId"/> to decide what the local player sees, then hides itself
	/// when the round returns to Lobby.
	///
	/// MonoBehaviour by design — pure view. All authoritative state (who won, when the match ends) lives
	/// on <see cref="MatchManager"/> / <see cref="TeamManager"/>; this only renders it. The overlay is built
	/// at runtime so the component is drop-in: attach it to any always-active GameObject in 03_Shooter
	/// (e.g. the UI canvas object) and it needs no further scene wiring.
	///
	/// Like <see cref="UIMatchLobby"/> this MUST sit on an always-active object — an inactive GameObject
	/// stops running Update and would never notice the phase reaching MatchOver.
	/// </summary>
	public sealed class UIMatchResult : MonoBehaviour
	{
		/// <summary>True on this peer while the result overlay is showing (Phase == MatchOver). <see cref="UIGameMenu"/>
		/// reads this to yield cursor/Escape control, mirroring <see cref="UIMatchLobby.IsLobbyOpen"/>.</summary>
		public static bool IsResultOpen { get; private set; }

		[Header("Colours")]
		public Color VictoryColor = new Color(0.30f, 0.95f, 0.45f);
		public Color DefeatColor  = new Color(1f, 0.30f, 0.28f);
		public Color NeutralColor = new Color(0.85f, 0.85f, 0.90f);
		[Tooltip("Full-screen backdrop tint behind the result text.")]
		public Color BackdropColor = new Color(0f, 0f, 0f, 0.72f);

		[Header("Layout")]
		[Tooltip("Sorting order of the runtime overlay canvas. Keep above the gameplay HUD.")]
		public int CanvasSortOrder = 200;
		public float TitleFontSize    = 110f;
		public float SubtitleFontSize = 40f;

		private CanvasGroup _group;
		private TextMeshProUGUI _title;
		private TextMeshProUGUI _subtitle;
		private GameManager _gameManager;

		private void Awake()
		{
			BuildOverlay();
			Show(false);
		}

		// Build a self-contained screen-space overlay (own Canvas) so the component needs no scene wiring.
		private void BuildOverlay()
		{
			var canvasGo = new GameObject("MatchResultCanvas");
			canvasGo.transform.SetParent(transform, false);

			var canvas = canvasGo.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = CanvasSortOrder;

			var scaler = canvasGo.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.matchWidthOrHeight = 0.5f;

			_group = canvasGo.AddComponent<CanvasGroup>();
			_group.interactable = false;     // display only — no buttons to click through
			_group.blocksRaycasts = false;

			// Full-screen backdrop.
			var bg = new GameObject("Backdrop").AddComponent<Image>();
			bg.transform.SetParent(canvasGo.transform, false);
			bg.color = BackdropColor;
			Stretch(bg.rectTransform);

			_title = MakeLabel(canvasGo.transform, "Title", TitleFontSize, FontStyles.Bold);
			Anchor(_title.rectTransform, new Vector2(0.5f, 0.58f), new Vector2(1600f, 220f));

			_subtitle = MakeLabel(canvasGo.transform, "Subtitle", SubtitleFontSize, FontStyles.Normal);
			Anchor(_subtitle.rectTransform, new Vector2(0.5f, 0.42f), new Vector2(1400f, 200f));
		}

		private static TextMeshProUGUI MakeLabel(Transform parent, string name, float size, FontStyles style)
		{
			var label = new GameObject(name).AddComponent<TextMeshProUGUI>();
			label.transform.SetParent(parent, false);
			label.fontSize = size;          // TMP falls back to the default font asset when none is assigned
			label.fontStyle = style;
			label.alignment = TextAlignmentOptions.Center;
			label.enableWordWrapping = true;
			return label;
		}

		private static void Stretch(RectTransform rt)
		{
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;
		}

		private static void Anchor(RectTransform rt, Vector2 anchor, Vector2 size)
		{
			rt.anchorMin = rt.anchorMax = anchor;
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.anchoredPosition = Vector2.zero;
			rt.sizeDelta = size;
		}

		private void Update()
		{
			var match = MatchManager.Instance;
			bool over = match != null && match.Phase == MatchPhase.MatchOver;

			if (over == false)
			{
				if (IsResultOpen) Show(false);
				return;
			}

			if (IsResultOpen == false) Show(true);

			// The result screen is modal — free the cursor so it reads as a screen, not gameplay.
			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = true;

			Render(match);
		}

		private void Render(MatchManager match)
		{
			int winningTeam = match.WinningTeamId;
			int myTeam = LocalTeam();

			if (winningTeam < 0)
			{
				// Simultaneous wipe / no kills on timer expiry — nobody is left standing.
				_title.color = NeutralColor;
				_title.text  = "MATCH OVER";
				_subtitle.text = "No survivors.";
			}
			else if (myTeam == winningTeam)
			{
				_title.color = VictoryColor;
				_title.text  = "VICTORY";
				_subtitle.text = "Last team standing.";
			}
			else
			{
				_title.color = DefeatColor;
				_title.text  = "DEFEAT";
				_subtitle.text = $"Team {winningTeam + 1} wins.";
			}

			int secs = Mathf.CeilToInt(Mathf.Max(0f, match.RemainingPhaseSeconds));
			if (secs > 0)
				_subtitle.text += $"\n\nReturning to lobby in {secs}…";
		}

		// The team the local player landed on, or -1 if spectating / not yet assigned.
		private int LocalTeam()
		{
			var teams = TeamManager.Instance;
			if (teams == null) return -1;

			if (_gameManager == null) _gameManager = FindAnyObjectByType<GameManager>();
			var local = _gameManager != null ? _gameManager.LocalPlayer : null;
			if (local == null || local.Object == null) return -1;

			return teams.TeamOf(local.Object.InputAuthority);
		}

		private void Show(bool show)
		{
			IsResultOpen = show;
			if (_group != null) _group.alpha = show ? 1f : 0f;
		}

		private void OnDisable()
		{
			IsResultOpen = false;
		}
	}
}
