using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.Shooter
{
	/// <summary>
	/// World-space keypad face for a <see cref="Padlock"/> station. Plain MonoBehaviour — local
	/// visual only, no networked state — living on the Padlock prefab next to the
	/// <see cref="Padlock"/> NetworkBehaviour. Builds its canvas procedurally on the keypad surface
	/// (mirrors <see cref="ComputerScreen"/>) so there's nothing to author in the Inspector beyond a
	/// <see cref="KeypadAnchor"/> transform pointing at the face.
	///
	/// Two states, toggled per-client:
	///   - <b>Idle</b>: a LOCKED / UNLOCKED label shown to everyone, reflecting the networked
	///     <see cref="Padlock.IsUnlocked"/>.
	///   - <b>Keypad</b>: the interactive 1-9 keypad + Confirm, shown ONLY on the local client whose
	///     <see cref="PadlockSession"/> currently has THIS padlock open.
	///
	/// Typing is local. Confirm forwards to <see cref="PadlockSession.Submit"/>; the authoritative
	/// result comes back through <see cref="Padlock.SubmitResult"/> and drives the green/red flash.
	/// </summary>
	[RequireComponent(typeof(Padlock))]
	public sealed class PadlockScreen : MonoBehaviour
	{
		[Header("Keypad Surface")]
		[Tooltip("Transform on the keypad face the canvas mounts to. Its +Z points INTO the face and +Y is up — the player docks on the opposite side. Canvas is built centered on it.")]
		public Transform KeypadAnchor;

		[Tooltip("World-space size of the keypad, in metres (width x height).")]
		public Vector2 SizeMeters = new Vector2(0.22f, 0.30f);

		[Header("Style")]
		public Color Background = new Color(0.10f, 0.11f, 0.13f, 1f);
		public Color DisplayColor = new Color(0.05f, 0.06f, 0.07f, 1f);
		public Color Foreground = new Color(0.9f, 0.95f, 1f, 1f);
		public Color ButtonColor = new Color(0.18f, 0.20f, 0.24f, 1f);
		public Color ConfirmColor = new Color(0.16f, 0.34f, 0.20f, 1f);
		public Color ClearColor = new Color(0.34f, 0.18f, 0.16f, 1f);
		public Color CorrectColor = new Color(0.20f, 0.75f, 0.30f, 1f);
		public Color WrongColor = new Color(0.80f, 0.20f, 0.20f, 1f);

		[Tooltip("Mask entered digits with • instead of showing them.")]
		public bool MaskEntry = false;

		[Tooltip("Seconds the display stays flashed green/red after a submit.")]
		[Min(0f)] public float FlashDuration = 0.8f;

		private const float PixelsPerMeter = 1200f;

		private Padlock _padlock;
		private PadlockSession _session;

		private Canvas _canvas;
		private GameObject _idleRoot;
		private GameObject _keypadRoot;
		private TMP_Text _idleLabel;
		private TMP_Text _displayText;
		private Image _displayBg;

		private readonly StringBuilder _entry = new();
		private float _flashTimer;
		private Color _displayBaseColor;
		private bool _locked; // true once unlocked: keypad disabled
		private bool _idleSynced; // becomes true once the idle label has reflected spawned state

		// Networked state on the Padlock can only be read once its NetworkObject has spawned.
		private bool PadlockSpawned => _padlock != null && _padlock.Object != null && _padlock.Object.IsValid;

		private void Awake()
		{
			_padlock = GetComponent<Padlock>();
			_displayBaseColor = DisplayColor;
			BuildCanvas();
			ShowKeypad(false);
			RefreshIdle();
		}

		private void OnEnable()
		{
			if (_padlock != null)
				_padlock.UnlockedChanged += OnUnlockedChanged;
		}

		private void OnDisable()
		{
			if (_padlock != null)
				_padlock.UnlockedChanged -= OnUnlockedChanged;
		}

		private void Update()
		{
			// Reflect the networked unlock state as soon as the padlock spawns. This covers the
			// initial value for late-joiners, where the OnChanged callback may not fire.
			if (!_idleSynced && PadlockSpawned)
			{
				_idleSynced = true;
				RefreshIdle();
			}

			if (_session == null)
			{
				TryBind();
				return;
			}

			if (_flashTimer > 0f)
			{
				_flashTimer -= Time.unscaledDeltaTime;
				if (_flashTimer <= 0f && _displayBg != null)
					_displayBg.color = _displayBaseColor;
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

			var session = gm.LocalPlayer.GetComponent<PadlockSession>();
			if (session == null) return;

			_session = session;
			_session.OpenedChanged += OnOpenedChanged;
			OnOpenedChanged(_session.Current);
		}

		private void OnOpenedChanged(Padlock open)
		{
			bool mine = open != null && open == _padlock;
			ShowKeypad(mine);
			if (mine)
			{
				_entry.Clear();
				_locked = _padlock.IsUnlocked;
				RefreshDisplay();
			}
		}

		// ─── Keypad behaviour ────────────────────────────────────────────────

		private void OnDigit(int digit)
		{
			if (_locked) return;
			if (_entry.Length >= Mathf.Max(1, _padlock.MaxLength)) return;
			_entry.Append((char)('0' + digit));
			RefreshDisplay();
		}

		private void OnClear()
		{
			if (_locked) return;
			_entry.Clear();
			RefreshDisplay();
		}

		private void OnConfirm()
		{
			if (_locked) return;
			if (_entry.Length == 0) return;

			string code = _entry.ToString();
			// The code is serialized on every peer, so validate locally for the green/red flash.
			// The authoritative networked unlock (and OnUnlocked event) is gated again on the host.
			bool correct = code == _padlock.Code;
			Flash(correct ? CorrectColor : WrongColor);
			if (correct)
			{
				_locked = true;
				_session?.Submit(code);
			}
			else
			{
				_entry.Clear();
				RefreshDisplay();
			}
		}

		private void OnUnlockedChanged()
		{
			RefreshIdle();
			if (_keypadRoot != null && _keypadRoot.activeSelf)
			{
				_locked = true;
				Flash(CorrectColor);
			}
		}

		private void Flash(Color color)
		{
			if (_displayBg == null) return;
			_displayBg.color = color;
			_flashTimer = FlashDuration;
		}

		private void RefreshDisplay()
		{
			if (_displayText == null) return;
			int n = _entry.Length;
			if (n == 0)
			{
				_displayText.text = "----";
				return;
			}
			if (MaskEntry)
			{
				_displayText.text = new string('•', n);
			}
			else
			{
				_displayText.text = _entry.ToString();
			}
		}

		private void RefreshIdle()
		{
			if (_idleLabel == null) return;
			// IsUnlocked is networked — only readable after the NetworkObject has spawned.
			// Before spawn, default to the locked appearance; Update() re-syncs once spawned.
			bool unlocked = PadlockSpawned && _padlock.IsUnlocked;
			_idleLabel.text = unlocked ? "● UNLOCKED" : "● LOCKED";
			_idleLabel.color = unlocked ? CorrectColor : new Color(0.8f, 0.7f, 0.3f, 1f);
		}

		// ─── Canvas build (procedural, world-space) ──────────────────────────

		private void BuildCanvas()
		{
			Transform anchor = KeypadAnchor != null ? KeypadAnchor : transform;

			var canvasGO = new GameObject("PadlockCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
			canvasGO.transform.SetParent(anchor, false);
			canvasGO.transform.localPosition = Vector3.zero;
			canvasGO.transform.localRotation = Quaternion.identity;

			_canvas = canvasGO.GetComponent<Canvas>();
			_canvas.renderMode = RenderMode.WorldSpace;

			var crt = (RectTransform)canvasGO.transform;
			crt.sizeDelta = SizeMeters * PixelsPerMeter;
			crt.localPosition = new Vector3(0f, 0f, -0.001f);
			crt.localScale = Vector3.one / PixelsPerMeter;

			BuildIdle(canvasGO.transform);
			BuildKeypad(canvasGO.transform);
		}

		private void BuildIdle(Transform parent)
		{
			_idleRoot = new GameObject("Idle", typeof(RectTransform), typeof(Image));
			_idleRoot.transform.SetParent(parent, false);
			StretchFull((RectTransform)_idleRoot.transform);
			_idleRoot.GetComponent<Image>().color = Background;

			_idleLabel = AddText(_idleRoot.transform, "IdleLabel", "● LOCKED", SizeMeters.y * PixelsPerMeter * 0.08f,
				TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
		}

		private void BuildKeypad(Transform parent)
		{
			_keypadRoot = new GameObject("Keypad", typeof(RectTransform), typeof(Image));
			_keypadRoot.transform.SetParent(parent, false);
			StretchFull((RectTransform)_keypadRoot.transform);
			_keypadRoot.GetComponent<Image>().color = Background;

			float pxW = SizeMeters.x * PixelsPerMeter;
			float pxH = SizeMeters.y * PixelsPerMeter;
			float pad = pxW * 0.06f;
			float displayH = pxH * 0.16f;

			// Display field across the top.
			var disp = new GameObject("Display", typeof(RectTransform), typeof(Image));
			disp.transform.SetParent(_keypadRoot.transform, false);
			var drt = (RectTransform)disp.transform;
			drt.anchorMin = new Vector2(0f, 1f);
			drt.anchorMax = new Vector2(1f, 1f);
			drt.pivot = new Vector2(0.5f, 1f);
			drt.sizeDelta = new Vector2(-(pad * 2f), displayH);
			drt.anchoredPosition = new Vector2(0f, -pad);
			_displayBg = disp.GetComponent<Image>();
			_displayBg.color = _displayBaseColor;

			_displayText = AddText(disp.transform, "Value", "----", displayH * 0.5f, TextAlignmentOptions.Center,
				Vector2.zero, Vector2.one);

			// Grid area below the display: 3 columns x 4 rows (1-9, then Clear / 0-less / Confirm).
			float gridTop = pad + displayH + pad;             // distance from top to grid start
			float gridBottom = pad;
			float gridLeft = pad;
			float gridRight = pad;
			float gridW = pxW - gridLeft - gridRight;
			float gridH = pxH - gridTop - gridBottom;

			int cols = 3;
			int rows = 4;
			float gap = pad * 0.5f;
			float cellW = (gridW - gap * (cols - 1)) / cols;
			float cellH = (gridH - gap * (rows - 1)) / rows;

			// Origin: bottom-left of the grid, measured from the keypad's bottom-left corner.
			// anchoredPosition for our buttons uses bottom-left pivot/anchor.
			float originX = gridLeft;
			float originY = gridBottom;

			// Rows top→bottom: [1 2 3][4 5 6][7 8 9][CLR _ OK].
			// Row index 0 is the top grid row, so its Y is highest.
			for (int r = 0; r < 3; r++)
			{
				for (int c = 0; c < cols; c++)
				{
					int digit = r * 3 + c + 1; // 1..9
					float x = originX + c * (cellW + gap);
					float y = originY + (rows - 1 - r) * (cellH + gap);
					int captured = digit;
					MakeButton(digit.ToString(), ButtonColor, x, y, cellW, cellH, () => OnDigit(captured));
				}
			}

			// Bottom row: Clear | (gap) | Confirm.
			float by = originY; // bottom-most row
			MakeButton("CLR", ClearColor, originX, by, cellW, cellH, OnClear);
			// Middle cell left intentionally empty (no 0 key — keypad is 1-9 per design).
			MakeButton("OK", ConfirmColor, originX + 2 * (cellW + gap), by, cellW, cellH, OnConfirm);
		}

		private void MakeButton(string label, Color color, float x, float y, float width, float height, UnityEngine.Events.UnityAction onClick)
		{
			var go = new GameObject($"Btn_{label}", typeof(RectTransform), typeof(Image), typeof(Button));
			go.transform.SetParent(_keypadRoot.transform, false);
			var rt = (RectTransform)go.transform;
			rt.anchorMin = new Vector2(0f, 0f);
			rt.anchorMax = new Vector2(0f, 0f);
			rt.pivot = new Vector2(0f, 0f);
			rt.sizeDelta = new Vector2(width, height);
			rt.anchoredPosition = new Vector2(x, y);

			var bg = go.GetComponent<Image>();
			bg.color = color;

			var btn = go.GetComponent<Button>();
			btn.targetGraphic = bg;
			btn.onClick.AddListener(onClick);

			var text = AddText(go.transform, "Label", label, height * 0.45f, TextAlignmentOptions.Center,
				Vector2.zero, Vector2.one);
			text.raycastTarget = false;
		}

		private void ShowKeypad(bool keypad)
		{
			if (_idleRoot != null) _idleRoot.SetActive(!keypad);
			if (_keypadRoot != null) _keypadRoot.SetActive(keypad);

			if (keypad && _canvas != null)
			{
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
		[Tooltip("Reference FOV used by 'Frame Camera View' to place the Padlock's KeypadViewTransform. Match your player camera's base FOV.")]
		public float ReferenceFieldOfView = 60f;

		[Tooltip("Fraction of the viewport height the keypad should fill when docked (0.8 = 80%).")]
		[Range(0.3f, 1f)] public float FillFraction = 0.8f;

		/// <summary>
		/// Positions the sibling <see cref="Padlock.KeypadViewTransform"/> directly in front of the
		/// keypad face at the distance that makes it fill <see cref="FillFraction"/> of the viewport
		/// height. Right-click the component header → "Frame Camera View".
		/// </summary>
		[ContextMenu("Frame Camera View")]
		private void FrameCameraView()
		{
			var padlock = GetComponent<Padlock>();
			Transform anchor = KeypadAnchor != null ? KeypadAnchor : transform;
			if (padlock == null || padlock.KeypadViewTransform == null)
			{
				Debug.LogWarning("[PadlockScreen] Assign Padlock.KeypadViewTransform first.", this);
				return;
			}

			float halfFov = Mathf.Deg2Rad * ReferenceFieldOfView * 0.5f;
			float targetHeight = SizeMeters.y / Mathf.Max(0.05f, FillFraction);
			float distance = targetHeight * 0.5f / Mathf.Tan(halfFov);

			var view = padlock.KeypadViewTransform;
			view.position = anchor.position - anchor.forward * distance;
			view.rotation = Quaternion.LookRotation(anchor.forward, anchor.up);
			UnityEditor.EditorUtility.SetDirty(view);
			Debug.Log($"[PadlockScreen] Placed KeypadViewTransform {distance:0.00}m from face (FOV {ReferenceFieldOfView}, fill {FillFraction:P0}).", this);
		}
#endif
	}
}
