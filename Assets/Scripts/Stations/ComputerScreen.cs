using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Starter.Shooter
{
	/// <summary>
	/// World-space monitor screen for a <see cref="Computer"/> station. Plain MonoBehaviour —
	/// local visual only, no networked state — living on the Computer prefab next to the
	/// <see cref="Computer"/> NetworkBehaviour. Builds its canvas procedurally on the screen
	/// surface (mirrors <see cref="CraftingHUD"/>'s build-in-code style) so there's no canvas to
	/// author in the Inspector — only a <see cref="ScreenAnchor"/> transform to point at the glass.
	///
	/// Two states, toggled per-client:
	///   - <b>Idle</b>: a "powered on" image, shown all the time, to everyone. This is what other
	///     players see while someone is using the machine.
	///   - <b>Terminal</b>: the interactive single-purpose terminal, shown ONLY on the local client
	///     whose <see cref="ComputerSession"/> currently has THIS computer open.
	///
	/// Because each client runs its own copy of the scene object, the toggle is driven entirely by
	/// the local <see cref="ComputerSession.OpenedChanged"/> event — no replication needed. Clicks
	/// work because <see cref="ComputerSession"/> docks the camera right in front of the glass and
	/// switches to the inventory input context, which frees the cursor.
	/// </summary>
	[RequireComponent(typeof(Computer))]
	public sealed class ComputerScreen : MonoBehaviour
	{
		[Serializable]
		public struct TerminalButton
		{
			[Tooltip("Caption drawn on the button.")]
			public string Label;

			[Tooltip("Raised when the button is clicked. Wire real behaviour here; if left empty the press is just echoed to the screen log.")]
			public UnityEvent OnPressed;
		}

		[Header("Screen Surface")]
		[Tooltip("Transform on the monitor glass the canvas mounts to. Its +Z points INTO the screen (away from the viewer) and +Y is up — the player docks on the opposite side and reads the UI. Canvas is built centered on it.")]
		public Transform ScreenAnchor;

		[Tooltip("World-space size of the screen, in metres (width x height). Match the actual glass of the model.")]
		public Vector2 ScreenSizeMeters = new Vector2(0.52f, 0.30f);

		[Header("Idle (everyone, always)")]
		[Tooltip("Optional 'powered on' image filling the screen when no local player is using it. If null, a solid colour + label is drawn instead.")]
		public Sprite IdleImage;

		public Color IdleBackground = new Color(0.03f, 0.06f, 0.12f, 1f);

		[Tooltip("Text shown over the idle screen when no IdleImage is set.")]
		public string IdleLabel = "● ONLINE";

		[Header("Terminal (local user only)")]
		public string Title = "TERMINAL";

		public Color ScreenBackground = new Color(0.02f, 0.05f, 0.04f, 1f);
		public Color Foreground = new Color(0.55f, 1f, 0.6f, 1f);
		public Color HeaderColor = new Color(0.1f, 0.18f, 0.14f, 1f);
		public Color ButtonColor = new Color(0.08f, 0.22f, 0.16f, 1f);

		[Tooltip("Lines printed when the terminal boots. Leave empty for a default banner.")]
		[TextArea] public string BootText = "";

		[Tooltip("Action buttons across the bottom of the terminal. If empty, a couple of demo buttons are created so the screen is interactive out of the box.")]
		public List<TerminalButton> Buttons = new List<TerminalButton>();

		// --- runtime build constants ---
		private const float PixelsPerMeter = 1200f; // canvas resolution; world size = sizeDelta / PPM
		private const int MaxLogLines = 18;
		private const int MaxButtons = 5;

		private Computer _computer;
		private ComputerSession _session;

		private Canvas _canvas;
		private GameObject _idleRoot;
		private GameObject _terminalRoot;
		private TMP_Text _logText;
		private TMP_Text _clockText;

		private readonly List<string> _log = new();
		private readonly StringBuilder _sb = new();
		private float _clockTimer;

		private void Awake()
		{
			_computer = GetComponent<Computer>();
			BuildCanvas();
			ShowTerminal(false);
		}

		private void Update()
		{
			// Bind to the local player's session once it has spawned (player spawns after scene objects).
			if (_session == null)
			{
				TryBind();
				return;
			}

			if (_terminalRoot != null && _terminalRoot.activeSelf && _clockText != null)
			{
				_clockTimer += Time.unscaledDeltaTime;
				if (_clockTimer >= 1f)
				{
					_clockTimer = 0f;
					_clockText.text = DateTime.Now.ToString("HH:mm:ss");
				}
			}
		}

		private void OnDestroy()
		{
			if (_session != null) _session.OpenedChanged -= OnOpenedChanged;
		}

		private void TryBind()
		{
			var gm = FindAnyObjectByType<GameManager>();
			if (gm == null || gm.LocalPlayer == null) return;

			var session = gm.LocalPlayer.GetComponent<ComputerSession>();
			if (session == null) return;

			_session = session;
			_session.OpenedChanged += OnOpenedChanged;

			// In case the session is already on this computer when we bind (late spawn), sync immediately.
			OnOpenedChanged(_session.Current);
		}

		private void OnOpenedChanged(Computer open)
		{
			bool mine = open != null && open == _computer;
			ShowTerminal(mine);
			if (mine) Boot();
		}

		// ─── Terminal behaviour ──────────────────────────────────────────────

		private void Boot()
		{
			_log.Clear();
			if (string.IsNullOrWhiteSpace(BootText))
			{
				Print($"{Title} v1.0");
				Print("ready.");
			}
			else
			{
				foreach (var line in BootText.Split('\n'))
					Print(line.TrimEnd('\r'));
			}
			RefreshLog();
			if (_clockText != null) _clockText.text = DateTime.Now.ToString("HH:mm:ss");
		}

		/// <summary>Append a line to the on-screen log. Public so wired button handlers can write to the screen.</summary>
		public void Print(string line)
		{
			_log.Add(line ?? string.Empty);
			while (_log.Count > MaxLogLines) _log.RemoveAt(0);
			RefreshLog();
		}

		/// <summary>Clear the on-screen log.</summary>
		public void ClearLog()
		{
			_log.Clear();
			RefreshLog();
		}

		private void RefreshLog()
		{
			if (_logText == null) return;
			_sb.Clear();
			for (int i = 0; i < _log.Count; i++)
			{
				if (i > 0) _sb.Append('\n');
				_sb.Append("> ").Append(_log[i]);
			}
			_logText.text = _sb.ToString();
		}

		private void OnButtonPressed(int index)
		{
			if (index < 0 || index >= Buttons.Count) return;
			var btn = Buttons[index];

			if (btn.OnPressed != null && btn.OnPressed.GetPersistentEventCount() > 0)
				btn.OnPressed.Invoke();
			else
				Print($"{btn.Label}");
		}

		private void OnPowerOff()
		{
			_session?.RequestClose();
		}

		// ─── Canvas build (procedural, world-space) ──────────────────────────

		private void BuildCanvas()
		{
			Transform anchor = ScreenAnchor != null ? ScreenAnchor : transform;

			var canvasGO = new GameObject("ComputerScreenCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
			canvasGO.transform.SetParent(anchor, false);
			canvasGO.transform.localPosition = Vector3.zero;
			canvasGO.transform.localRotation = Quaternion.identity;

			_canvas = canvasGO.GetComponent<Canvas>();
			_canvas.renderMode = RenderMode.WorldSpace;

			var crt = (RectTransform)canvasGO.transform;
			crt.sizeDelta = ScreenSizeMeters * PixelsPerMeter;
			// Push the canvas a hair toward the viewer (local -Z, since +Z faces into the glass) so it
			// never z-fights with the model's screen face.
			crt.localPosition = new Vector3(0f, 0f, -0.001f);
			crt.localScale = Vector3.one / PixelsPerMeter;

			BuildIdle(canvasGO.transform);
			BuildTerminal(canvasGO.transform);
		}

		private void BuildIdle(Transform parent)
		{
			_idleRoot = new GameObject("Idle", typeof(RectTransform), typeof(Image));
			_idleRoot.transform.SetParent(parent, false);
			StretchFull((RectTransform)_idleRoot.transform);

			var img = _idleRoot.GetComponent<Image>();
			if (IdleImage != null)
			{
				img.sprite = IdleImage;
				img.color = Color.white;
				img.preserveAspect = true;
			}
			else
			{
				img.color = IdleBackground;
				var label = AddText(_idleRoot.transform, "IdleLabel", IdleLabel, 64, TextAlignmentOptions.Center,
					Vector2.zero, Vector2.one);
				label.color = new Color(0.4f, 0.8f, 1f, 1f);
			}
		}

		private void BuildTerminal(Transform parent)
		{
			_terminalRoot = new GameObject("Terminal", typeof(RectTransform), typeof(Image));
			_terminalRoot.transform.SetParent(parent, false);
			StretchFull((RectTransform)_terminalRoot.transform);
			_terminalRoot.GetComponent<Image>().color = ScreenBackground;

			float pxW = ScreenSizeMeters.x * PixelsPerMeter;
			float pxH = ScreenSizeMeters.y * PixelsPerMeter;
			float headerH = pxH * 0.12f;
			float buttonH = pxH * 0.16f;
			float pad = pxW * 0.03f;

			// Header bar
			var header = new GameObject("Header", typeof(RectTransform), typeof(Image));
			header.transform.SetParent(_terminalRoot.transform, false);
			var hrt = (RectTransform)header.transform;
			hrt.anchorMin = new Vector2(0f, 1f);
			hrt.anchorMax = new Vector2(1f, 1f);
			hrt.pivot = new Vector2(0.5f, 1f);
			hrt.sizeDelta = new Vector2(0f, headerH);
			hrt.anchoredPosition = Vector2.zero;
			header.GetComponent<Image>().color = HeaderColor;

			var title = AddText(header.transform, "Title", Title, headerH * 0.5f, TextAlignmentOptions.MidlineLeft,
				Vector2.zero, Vector2.one);
			title.rectTransform.offsetMin = new Vector2(pad, 0f);
			title.rectTransform.offsetMax = new Vector2(-pad, 0f);

			_clockText = AddText(header.transform, "Clock", "--:--:--", headerH * 0.45f, TextAlignmentOptions.MidlineRight,
				Vector2.zero, Vector2.one);
			_clockText.rectTransform.offsetMin = new Vector2(pad, 0f);
			_clockText.rectTransform.offsetMax = new Vector2(-pad, 0f);

			// Log area (fills between header and the button row)
			_logText = AddText(_terminalRoot.transform, "Log", string.Empty, pxH * 0.05f, TextAlignmentOptions.TopLeft,
				Vector2.zero, Vector2.one);
			var lrt = _logText.rectTransform;
			lrt.offsetMin = new Vector2(pad, buttonH + pad);
			lrt.offsetMax = new Vector2(-pad, -(headerH + pad * 0.5f));
			_logText.textWrappingMode = TextWrappingModes.Normal;
			_logText.overflowMode = TextOverflowModes.Truncate;

			// Button row along the bottom: configured buttons + a trailing POWER OFF.
			EnsureDefaultButtons();
			int count = Mathf.Min(Buttons.Count, MaxButtons);
			int total = count + 1; // +1 for POWER OFF
			float gap = pad;
			float rowWidth = pxW - pad * 2f;
			float btnWidth = (rowWidth - gap * (total - 1)) / total;

			for (int i = 0; i < count; i++)
			{
				int captured = i;
				MakeButton(Buttons[i].Label, ButtonColor, pad + i * (btnWidth + gap), btnWidth, buttonH, pad,
					() => OnButtonPressed(captured));
			}

			MakeButton("POWER OFF", new Color(0.3f, 0.08f, 0.08f, 1f),
				pad + count * (btnWidth + gap), btnWidth, buttonH, pad, OnPowerOff);
		}

		private void EnsureDefaultButtons()
		{
			if (Buttons != null && Buttons.Count > 0) return;
			Buttons = new List<TerminalButton>
			{
				new TerminalButton { Label = "STATUS" },
				new TerminalButton { Label = "PING" },
			};
		}

		private void MakeButton(string label, Color color, float x, float width, float height, float bottom, UnityAction onClick)
		{
			var go = new GameObject($"Btn_{label}", typeof(RectTransform), typeof(Image), typeof(Button));
			go.transform.SetParent(_terminalRoot.transform, false);
			var rt = (RectTransform)go.transform;
			rt.anchorMin = new Vector2(0f, 0f);
			rt.anchorMax = new Vector2(0f, 0f);
			rt.pivot = new Vector2(0f, 0f);
			rt.sizeDelta = new Vector2(width, height);
			rt.anchoredPosition = new Vector2(x, bottom);

			var bg = go.GetComponent<Image>();
			bg.color = color;

			var btn = go.GetComponent<Button>();
			btn.targetGraphic = bg;
			btn.onClick.AddListener(onClick);

			var text = AddText(go.transform, "Label", label, height * 0.4f, TextAlignmentOptions.Center,
				Vector2.zero, Vector2.one);
			text.raycastTarget = false;
		}

		private void ShowTerminal(bool terminal)
		{
			if (_idleRoot != null) _idleRoot.SetActive(!terminal);
			if (_terminalRoot != null) _terminalRoot.SetActive(terminal);

			if (terminal && _canvas != null)
			{
				// World-space GraphicRaycaster needs an event camera; the docked camera is Camera.main.
				_canvas.worldCamera = Camera.main;
			}
		}

		private static void StretchFull(RectTransform rt)
		{
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;
		}

		private TMP_Text AddText(Transform parent, string name, string text, float fontSize,
			TextAlignmentOptions alignment, Vector2 anchorMin, Vector2 anchorMax)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
			go.transform.SetParent(parent, false);
			var t = go.GetComponent<TextMeshProUGUI>();
			t.text = text;
			t.fontSize = fontSize;
			t.alignment = alignment;
			t.color = Foreground;
			t.raycastTarget = false;
			var rt = t.rectTransform;
			rt.anchorMin = anchorMin;
			rt.anchorMax = anchorMax;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;
			return t;
		}

#if UNITY_EDITOR
		[Header("Editor helper")]
		[Tooltip("Reference FOV used by 'Frame Camera View' to place the Computer's ScreenViewTransform. Match your player camera's base FOV.")]
		public float ReferenceFieldOfView = 60f;

		[Tooltip("Fraction of the viewport height the screen should fill when docked (0.8 = 80%).")]
		[Range(0.3f, 1f)] public float FillFraction = 0.8f;

		/// <summary>
		/// Positions the sibling <see cref="Computer.ScreenViewTransform"/> directly in front of the
		/// screen at the distance that makes the glass fill <see cref="FillFraction"/> of the viewport
		/// height, looking straight at it. Right-click the component header → "Frame Camera View".
		/// </summary>
		[ContextMenu("Frame Camera View")]
		private void FrameCameraView()
		{
			var computer = GetComponent<Computer>();
			Transform anchor = ScreenAnchor != null ? ScreenAnchor : transform;
			if (computer == null || computer.ScreenViewTransform == null)
			{
				Debug.LogWarning("[ComputerScreen] Assign Computer.ScreenViewTransform first.", this);
				return;
			}

			float halfFov = Mathf.Deg2Rad * ReferenceFieldOfView * 0.5f;
			float targetHeight = ScreenSizeMeters.y / Mathf.Max(0.05f, FillFraction);
			float distance = targetHeight * 0.5f / Mathf.Tan(halfFov);

			// ScreenAnchor +Z faces into the glass, so the viewer side is -anchor.forward. Place the
			// camera there and aim it back at the screen (looking along anchor.forward).
			var view = computer.ScreenViewTransform;
			view.position = anchor.position - anchor.forward * distance;
			view.rotation = Quaternion.LookRotation(anchor.forward, anchor.up);
			UnityEditor.EditorUtility.SetDirty(view);
			Debug.Log($"[ComputerScreen] Placed ScreenViewTransform {distance:0.00}m from screen (FOV {ReferenceFieldOfView}, fill {FillFraction:P0}).", this);
		}
#endif
	}
}
