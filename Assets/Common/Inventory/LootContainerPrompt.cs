using TMPro;
using UnityEngine;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// Local-only billboard sitting on a <see cref="LootContainer"/>. Displays
	/// "[E] Open" or "[E] (In use)" tracked off the container's CurrentUser.
	/// Builds its own world-space canvas + TMP text procedurally so the prefab
	/// only needs this component attached.
	/// </summary>
	[RequireComponent(typeof(LootContainer))]
	public sealed class LootContainerPrompt : MonoBehaviour
	{
		[Header("Layout")]
		public Vector3 LocalOffset = new Vector3(0f, 1.6f, 0f);
		public float CanvasScale = 0.01f;

		[Header("Text")]
		public string OpenText = "[E] Open";
		public string InUseText = "[E] (In use)";
		public Color FreeColor = new Color(0.8f, 0.95f, 1f, 1f);
		public Color InUseColor = new Color(1f, 0.6f, 0.3f, 1f);

		private LootContainer _container;
		private TMP_Text _text;
		private Transform _cameraTransform;
		private GameObject _canvasGO;

		private void Awake()
		{
			_container = GetComponent<LootContainer>();
			BuildCanvas();
		}

		private void OnEnable()
		{
			if (_container != null)
				_container.UserChanged += OnUserChanged;
		}

		private void OnDisable()
		{
			if (_container != null)
				_container.UserChanged -= OnUserChanged;
		}

		private void Start()
		{
			ApplyState(inUse: false);
		}

		private void LateUpdate()
		{
			if (_canvasGO == null) return;
			if (_cameraTransform == null && Camera.main != null)
				_cameraTransform = Camera.main.transform;
			if (_cameraTransform == null) return;

			_canvasGO.transform.rotation = _cameraTransform.rotation;
		}

		private void OnUserChanged(LootContainer source, Fusion.PlayerRef previous, Fusion.PlayerRef current)
		{
			ApplyState(inUse: current != Fusion.PlayerRef.None);
		}

		private void ApplyState(bool inUse)
		{
			if (_text == null) return;
			_text.text = inUse ? InUseText : OpenText;
			_text.color = inUse ? InUseColor : FreeColor;
		}

		private void BuildCanvas()
		{
			_canvasGO = new GameObject("LootPrompt");
			_canvasGO.transform.SetParent(transform, false);
			_canvasGO.transform.localPosition = LocalOffset;
			_canvasGO.transform.localScale = Vector3.one * CanvasScale;

			var canvas = _canvasGO.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.WorldSpace;

			var rt = _canvasGO.GetComponent<RectTransform>();
			rt.sizeDelta = new Vector2(200f, 50f);

			var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
			textGO.transform.SetParent(_canvasGO.transform, false);
			var trt = textGO.GetComponent<RectTransform>();
			trt.anchorMin = Vector2.zero;
			trt.anchorMax = Vector2.one;
			trt.offsetMin = Vector2.zero;
			trt.offsetMax = Vector2.zero;

			_text = textGO.GetComponent<TextMeshProUGUI>();
			_text.alignment = TextAlignmentOptions.Center;
			_text.fontSize = 28f;
			_text.text = OpenText;
			_text.color = FreeColor;
			_text.raycastTarget = false;
		}
	}
}
