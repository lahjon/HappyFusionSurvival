using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Starter
{
	/// <summary>
	/// Full-screen, animated loading overlay that survives scene transitions. It exists so the gap between leaving one
	/// networked scene and the next being fully ready (lobby → game, game → lobby) is covered by a clearly-animated
	/// screen rather than a frozen frame or a static image that gets destroyed mid-load.
	///
	/// Self-contained by design:
	///   * It builds its whole UI in code (Canvas + opaque background + spinner + label), so there are no prefabs to
	///     wire and nothing to break a Fusion bake — it is a plain local <see cref="MonoBehaviour"/>.
	///   * It auto-bootstraps (<see cref="Bootstrap"/>) and is <c>DontDestroyOnLoad</c>, like <c>MenuManager</c>, so it
	///     is always available and persists across the Single-mode scene swap that would otherwise destroy a scene-local
	///     overlay (e.g. <c>MenuBusyOverlay</c>).
	///   * The spinner and label animate off <see cref="Time.unscaledDeltaTime"/>, so whenever a frame *does* render
	///     during a load it is visibly moving — it never reads as frozen.
	///
	/// Driven by Fusion's scene-load callbacks via <see cref="BindRunner"/>: <c>OnSceneLoadStart</c> shows it and
	/// <c>OnSceneLoadDone</c> hides it, uniformly for host and clients. <see cref="Show"/>/<see cref="Hide"/> are also
	/// callable directly (e.g. the host shows it the instant the Start button is clicked, before the load even begins).
	/// </summary>
	public sealed class LoadingScreen : MonoBehaviour
	{
		public static LoadingScreen Instance { get; private set; }

		// Tuning (compile-time; this is auto-built so there is no Inspector to expose them on).
		private static readonly Color BackgroundColor = new Color(0.06f, 0.06f, 0.10f, 1f);
		private static readonly Color SpinnerColor = new Color(0.97f, 0.74f, 0.86f, 1f); // pastel pink, matches the town palette
		private const float FadeSpeed = 6f;             // CanvasGroup alpha units per second
		private const float RevolutionsPerSecond = 0.8f;
		private const int DotCount = 12;
		private const float HoldAfterLoad = 0.15f;      // brief grace after a load completes so spawns settle before fading

		private GameObject _canvasGO;
		private CanvasGroup _group;
		private TextMeshProUGUI _label;
		private RectTransform _spinner;
		private Image[] _dots;

		private string _message = "Loading";
		private bool _visibleTarget;
		private float _spin;
		private float _labelTimer;
		private int _labelDots;
		private float _hideAt = -1f; // unscaled time at which a pending (held) hide fires; <0 = none

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Bootstrap()
		{
			if (Instance != null)
				return;

			var go = new GameObject("LoadingScreen");
			DontDestroyOnLoad(go);
			Instance = go.AddComponent<LoadingScreen>();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			// Enter Play Mode Options can disable domain reload, so statics survive between Play sessions.
			Instance = null;
		}

		/// <summary>Bootstraps the singleton on demand so callers never race the AfterSceneLoad hook.</summary>
		private static LoadingScreen Ensure()
		{
			if (Instance == null)
				Bootstrap();
			return Instance;
		}

		/// <summary>
		/// Subscribe to a runner's scene-load events so the overlay shows for the whole transition and hides when the
		/// new scene is ready — on host and clients alike. Call once per runner, right after it is created.
		/// </summary>
		public static void BindRunner(NetworkRunner runner)
		{
			if (runner == null)
				return;

			var events = runner.GetComponent<NetworkEvents>();
			if (events == null)
				return;

			events.OnSceneLoadStart.AddListener(_ => Ensure().Show());
			events.OnSceneLoadDone.AddListener(_ => Ensure().HideAfterHold());
		}

		/// <summary>Show the overlay immediately. Optionally change the message line.</summary>
		public void Show(string message = null)
		{
			if (string.IsNullOrEmpty(message) == false)
				_message = message;

			_hideAt = -1f;
			_visibleTarget = true;

			if (_canvasGO == null)
				Build();
			_canvasGO.SetActive(true);
		}

		/// <summary>Begin fading the overlay out now.</summary>
		public void Hide()
		{
			_hideAt = -1f;
			_visibleTarget = false;
		}

		/// <summary>Hide after a brief grace period so freshly-spawned objects (player avatar, camera) settle under cover.</summary>
		private void HideAfterHold()
		{
			_hideAt = Time.unscaledTime + HoldAfterLoad;
		}

		private void OnDestroy()
		{
			if (Instance == this)
				Instance = null;
		}

		private void Update()
		{
			if (_canvasGO == null)
				return;

			if (_hideAt >= 0f && Time.unscaledTime >= _hideAt)
			{
				_hideAt = -1f;
				_visibleTarget = false;
			}

			// Fade toward the target alpha.
			float target = _visibleTarget ? 1f : 0f;
			_group.alpha = Mathf.MoveTowards(_group.alpha, target, FadeSpeed * Time.unscaledDeltaTime);
			_group.blocksRaycasts = _group.alpha > 0.01f; // absorb clicks the whole time it is on screen

			if (_visibleTarget == false && _group.alpha <= 0.001f)
			{
				_canvasGO.SetActive(false); // fully hidden — stop drawing until the next Show
				return;
			}

			// Spinner: a ring of dots with a "comet" highlight rotating around it (no sprite assets needed).
			_spin += RevolutionsPerSecond * Time.unscaledDeltaTime;
			float head = (_spin - Mathf.Floor(_spin)) * DotCount; // leading dot index, fractional
			for (int i = 0; i < _dots.Length; i++)
			{
				// Distance (in dot-steps) behind the rotating head, wrapped into [0, DotCount).
				float behind = head - i;
				behind -= Mathf.Floor(behind / DotCount) * DotCount;
				float t = 1f - behind / DotCount;        // 1 at the head, fading to 0 around the ring
				var c = SpinnerColor;
				c.a = 0.15f + 0.85f * (t * t);
				_dots[i].color = c;
			}

			// Animated "Loading" → "Loading..." dots so the text line also reads as alive.
			_labelTimer += Time.unscaledDeltaTime;
			if (_labelTimer >= 0.35f)
			{
				_labelTimer = 0f;
				_labelDots = (_labelDots + 1) % 4;
			}
			_label.text = _message + new string('.', _labelDots);
		}

		// =========================================================================
		// UI construction (all in code — no prefabs, no Inspector wiring)
		// =========================================================================

		private void Build()
		{
			_canvasGO = new GameObject("LoadingCanvas");
			_canvasGO.transform.SetParent(transform, false);

			var canvas = _canvasGO.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 30000; // above the pause menu and every gameplay/HUD canvas

			var scaler = _canvasGO.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.matchWidthOrHeight = 0.5f;

			_canvasGO.AddComponent<GraphicRaycaster>();

			_group = _canvasGO.AddComponent<CanvasGroup>();
			_group.alpha = 0f;
			_group.blocksRaycasts = false;

			// Opaque full-screen background (hides the half-loaded scene underneath).
			var bg = NewChild("Background", _canvasGO.transform);
			Stretch(bg);
			var bgImage = bg.gameObject.AddComponent<Image>();
			bgImage.color = BackgroundColor;
			bgImage.raycastTarget = true;

			// Spinner ring, centred and lifted slightly above the middle.
			var spinnerGO = NewChild("Spinner", bg);
			_spinner = spinnerGO;
			_spinner.anchorMin = _spinner.anchorMax = new Vector2(0.5f, 0.5f);
			_spinner.pivot = new Vector2(0.5f, 0.5f);
			_spinner.anchoredPosition = new Vector2(0f, 40f);
			_spinner.sizeDelta = new Vector2(120f, 120f);

			_dots = new Image[DotCount];
			const float radius = 50f;
			for (int i = 0; i < DotCount; i++)
			{
				float ang = (i / (float)DotCount) * Mathf.PI * 2f;
				var dot = NewChild("Dot" + i, _spinner);
				dot.anchorMin = dot.anchorMax = new Vector2(0.5f, 0.5f);
				dot.pivot = new Vector2(0.5f, 0.5f);
				dot.anchoredPosition = new Vector2(Mathf.Cos(ang) * radius, Mathf.Sin(ang) * radius);
				dot.sizeDelta = new Vector2(14f, 14f);
				var img = dot.gameObject.AddComponent<Image>();
				img.color = SpinnerColor;
				img.raycastTarget = false;
				_dots[i] = img;
			}

			// Message label below the spinner.
			var labelGO = NewChild("Label", bg);
			labelGO.anchorMin = labelGO.anchorMax = new Vector2(0.5f, 0.5f);
			labelGO.pivot = new Vector2(0.5f, 0.5f);
			labelGO.anchoredPosition = new Vector2(0f, -70f);
			labelGO.sizeDelta = new Vector2(800f, 80f);
			_label = labelGO.gameObject.AddComponent<TextMeshProUGUI>();
			_label.text = _message;
			_label.fontSize = 42f;
			_label.alignment = TextAlignmentOptions.Center;
			_label.color = Color.white;
			_label.raycastTarget = false;
		}

		private static RectTransform NewChild(string name, Transform parent)
		{
			var go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);
			return (RectTransform)go.transform;
		}

		private static void Stretch(RectTransform rt)
		{
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;
		}
	}
}
