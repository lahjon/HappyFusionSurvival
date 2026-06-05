using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-view-only roster shown on the left of the screen: one row per teammate (excludes the local player).
	/// Alive teammates show just their name; dead teammates show a skull next to a greyed-out name. Hidden entirely
	/// in Solo (no teammates) or before teams are assigned. Pure presentation — reads <see cref="Player.Owner"/>,
	/// <see cref="TeamManager.SameTeam"/>, <see cref="Health.IsAlive"/>.
	///
	/// Self-bootstrapping (see <see cref="Bootstrap"/>) and builds its own overlay canvas + rows at runtime, so
	/// there is no scene or prefab wiring.
	/// </summary>
	public sealed class TeamStatusPanel : MonoBehaviour
	{
		private const float RowFontSize = 22f;
		private static readonly Color AliveColor = new Color(0.92f, 0.96f, 1f, 1f);
		private static readonly Color DeadColor = new Color(0.55f, 0.55f, 0.6f, 1f);
		private static readonly Color SkullTint = new Color(0.85f, 0.85f, 0.9f, 1f);
		private static readonly Color PanelBg = new Color(0f, 0f, 0f, 0.45f);

		private static TeamStatusPanel _instance;

		private GameObject _root;
		private RectTransform _content;
		private TMP_FontAsset _font;
		private Sprite _skullSprite;

		private readonly List<Row> _rows = new(8);
		private readonly List<Player> _mates = new(8);

		private sealed class Row
		{
			public GameObject Go;
			public Image Skull;
			public TextMeshProUGUI Name;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Bootstrap()
		{
			if (_instance != null) return;
			var go = new GameObject("TeamStatusPanel");
			DontDestroyOnLoad(go);
			_instance = go.AddComponent<TeamStatusPanel>();
		}

		private void Awake()
		{
			if (_instance != null && _instance != this) { Destroy(gameObject); return; }
			_instance = this;

			_font = TMP_Settings.defaultFontAsset;
			_skullSprite = BuildSkullSprite();
			BuildCanvas();
		}

		private void Update()
		{
			var local = ResolveLocalPlayer();
			var team = TeamManager.Instance;

			_mates.Clear();
			if (local != null && team != null)
			{
				var roster = Player.All;
				for (int i = 0; i < roster.Count; i++)
				{
					var p = roster[i];
					if (p == null || p.Object == null || p == local) continue;
					if (team.SameTeam(local.Owner, p.Owner))
						_mates.Add(p);
				}
				// Stable order so rows don't reshuffle frame to frame.
				_mates.Sort((a, b) => a.Owner.PlayerId.CompareTo(b.Owner.PlayerId));
			}

			if (_mates.Count == 0)
			{
				if (_root.activeSelf) _root.SetActive(false);
				return;
			}
			if (_root.activeSelf == false) _root.SetActive(true);

			EnsureRowCount(_mates.Count);

			for (int i = 0; i < _rows.Count; i++)
			{
				var row = _rows[i];
				if (i >= _mates.Count)
				{
					if (row.Go.activeSelf) row.Go.SetActive(false);
					continue;
				}
				if (row.Go.activeSelf == false) row.Go.SetActive(true);

				var p = _mates[i];
				bool alive = p.Health != null && p.Health.IsAlive;
				row.Name.text = p.Nickname;
				row.Name.color = alive ? AliveColor : DeadColor;
				row.Name.fontStyle = alive ? FontStyles.Normal : FontStyles.Strikethrough;
				if (row.Skull.enabled != !alive) row.Skull.enabled = !alive;
			}
		}

		private void EnsureRowCount(int count)
		{
			while (_rows.Count < count)
				_rows.Add(CreateRow());
		}

		private Player ResolveLocalPlayer()
		{
			var roster = Player.All;
			for (int i = 0; i < roster.Count; i++)
			{
				var p = roster[i];
				if (p != null && p.Object != null && p.Object.HasInputAuthority)
					return p;
			}
			return null;
		}

		// =========================================================================
		// UI construction (runtime, no prefabs)
		// =========================================================================

		private void BuildCanvas()
		{
			var canvasGo = new GameObject("TeamPanelCanvas");
			canvasGo.transform.SetParent(transform, false);
			var canvas = canvasGo.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 50; // above the HUD, below the pause menu
			var scaler = canvasGo.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.matchWidthOrHeight = 0.5f;

			_root = new GameObject("TeamRoster");
			var rootRt = _root.AddComponent<RectTransform>();
			rootRt.SetParent(canvasGo.transform, false);
			rootRt.anchorMin = new Vector2(0f, 0.5f);
			rootRt.anchorMax = new Vector2(0f, 0.5f);
			rootRt.pivot = new Vector2(0f, 0.5f);
			rootRt.anchoredPosition = new Vector2(24f, 0f);

			var bg = _root.AddComponent<Image>();
			bg.color = PanelBg;

			var vlg = _root.AddComponent<VerticalLayoutGroup>();
			vlg.padding = new RectOffset(12, 16, 10, 10);
			vlg.spacing = 4f;
			vlg.childControlWidth = true;
			vlg.childControlHeight = true;
			vlg.childForceExpandWidth = false;
			vlg.childForceExpandHeight = false;
			vlg.childAlignment = TextAnchor.MiddleLeft;

			var fitter = _root.AddComponent<ContentSizeFitter>();
			fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
			fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			_content = rootRt;
			_root.SetActive(false);
		}

		private Row CreateRow()
		{
			var rowGo = new GameObject("Row");
			var rowRt = rowGo.AddComponent<RectTransform>();
			rowRt.SetParent(_content, false);

			var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
			hlg.spacing = 8f;
			hlg.childControlWidth = true;
			hlg.childControlHeight = true;
			hlg.childForceExpandWidth = false;
			hlg.childForceExpandHeight = false;
			hlg.childAlignment = TextAnchor.MiddleLeft;

			// Skull icon (shown only when dead).
			var skullGo = new GameObject("Skull");
			var skullRt = skullGo.AddComponent<RectTransform>();
			skullRt.SetParent(rowRt, false);
			var skull = skullGo.AddComponent<Image>();
			skull.sprite = _skullSprite;
			skull.color = SkullTint;
			skull.preserveAspect = true;
			var skullLe = skullGo.AddComponent<LayoutElement>();
			skullLe.preferredWidth = RowFontSize;
			skullLe.preferredHeight = RowFontSize;
			skull.enabled = false;

			// Name label.
			var nameGo = new GameObject("Name");
			var nameRt = nameGo.AddComponent<RectTransform>();
			nameRt.SetParent(rowRt, false);
			var label = nameGo.AddComponent<TextMeshProUGUI>();
			if (_font != null) label.font = _font;
			label.fontSize = RowFontSize;
			label.color = AliveColor;
			label.alignment = TextAlignmentOptions.MidlineLeft;
			label.textWrappingMode = TextWrappingModes.NoWrap;
			label.raycastTarget = false;

			return new Row { Go = rowGo, Skull = skull, Name = label };
		}

		/// <summary>Builds a small skull glyph as a sprite at runtime so the panel needs no imported icon and no
		/// dependency on a TMP font that may lack the ☠ glyph. White pixels, tinted by the Image colour.</summary>
		private static Sprite BuildSkullSprite()
		{
			// 16x16, '#' = opaque. Read top-to-bottom; flipped into the texture (origin bottom-left).
			string[] art =
			{
				"....########....",
				"..############..",
				".##############.",
				"################",
				"################",
				"####..####..####",
				"####..####..####",
				"################",
				"######....######",
				".##############.",
				".##############.",
				"..############..",
				"..############..",
				"..#.##.##.##.#..",
				"...#.#.#.#.#....",
				"................",
			};

			const int size = 16;
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
			var clear = new Color32(255, 255, 255, 0);
			var solid = new Color32(255, 255, 255, 255);
			for (int y = 0; y < size; y++)
			{
				// art[0] is the top row; texture row 0 is the bottom, so flip y.
				string line = art[size - 1 - y];
				for (int x = 0; x < size; x++)
					tex.SetPixel(x, y, x < line.Length && line[x] == '#' ? solid : clear);
			}
			tex.Apply();
			return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
		}
	}
}
