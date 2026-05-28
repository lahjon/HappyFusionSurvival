using UnityEngine;
using UnityEngine.UI;

namespace Starter.Common.Interactions
{
	/// <summary>
	/// Local-only camera-facing indicator that sits on any <see cref="IInteractable"/>.
	/// Renders a small box that fades in within <see cref="VisibilityRange"/> and
	/// brightens only when the local scanner has picked this object as its current
	/// target. Builds its own world-space canvas procedurally.
	/// </summary>
	public sealed class InteractionPrompt : MonoBehaviour
	{
		// Shared world size for every interaction indicator. Single source of truth so prefabs can't drift.
		public const float IndicatorWorldSize = 0.4f;

		private const float SizeDeltaPixels = 40f;
		private static readonly Vector2 IndicatorSize = new Vector2(SizeDeltaPixels, SizeDeltaPixels);
		private const float CanvasScale = IndicatorWorldSize / SizeDeltaPixels;

		[Header("Layout")]
		public Vector3 LocalOffset = Vector3.zero;
		[Tooltip("Distance (m) to push the indicator toward the camera each frame. With the always-on-top shader this can stay 0 — kept for fallback if you swap the shader out.")]
		public float CameraBias = 0f;
		[Tooltip("Sorting order for the indicator's canvas. Raise above other UI to keep it visible.")]
		public int CanvasSortOrder = 100;

		[Header("Ranges (m)")]
		[Tooltip("Indicator becomes visible (in disabled state) when the local camera is within this distance.")]
		public float VisibilityRange = 2f;
		[Tooltip("Indicator brightens to the active state when the local camera is within this distance (= player's pickup range).")]
		public float ActiveRange = 2f;

		[Header("Visibility")]
		[Tooltip("If true, the prompt hides whenever the attached IInteractable.CanInteract is false. Opt-in so existing prefabs (locked chests etc.) keep their always-visible behavior.")]
		public bool HideWhenLocked = false;

		[Header("Colors")]
		public Color ActiveColor = new Color(0.3f, 0.65f, 1f, 1f);
		public Color InactiveColor = new Color(0.3f, 0.65f, 1f, 0.35f);
		[Tooltip("Color of the hold-progress overlay drawn on top of the indicator while the player is holding Interact on a pickupable.")]
		public Color HoldColor = new Color(1f, 0.85f, 0.2f, 1f);

		private Transform _cameraTransform;
		private GameObject _canvasGO;
		private Image _box;
		private Image _holdFill;
		private IInteractable _interactable;

		private static Material _overlayMaterial;
		private static Material GetOverlayMaterial()
		{
			if (_overlayMaterial != null) return _overlayMaterial;
			var shader = Shader.Find("UI/InteractionPromptOverlay");
			if (shader == null) return null;
			_overlayMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
			return _overlayMaterial;
		}

		private void Awake()
		{
			_interactable = GetComponentInParent<IInteractable>();
			BuildCanvas();
			_canvasGO.SetActive(false);
		}

		private void OnDestroy()
		{
			if (_canvasGO != null) Destroy(_canvasGO);
		}

		private void LateUpdate()
		{
			if (_canvasGO == null) return;
			if (_cameraTransform == null && Camera.main != null)
				_cameraTransform = Camera.main.transform;
			if (_cameraTransform == null) return;

			// Distance used for visibility + size is measured from the local PLAYER,
			// not the camera — matches the InteractionScanner's range checks.
			var scanner = InteractionScanner.LocalInstance;
			Vector3 playerPos = scanner != null ? scanner.transform.position : _cameraTransform.position;
			float dist = Vector3.Distance(transform.position, playerPos);

			// Hide all prompts while the scanner is gated (crafting/loot/vehicle/sleep UI open,
			// cursor unlocked, etc.). Only enforce once a local scanner exists so prompts still
			// show in pre-spawn states.
			bool gated = scanner != null && !InteractionScanner.IsScanningActive;

			// Opt-in: hide when the underlying IInteractable can't be interacted with right now
			// (e.g. a player who is alive shouldn't show the "revive me" indicator). Default-off
			// so existing prefabs (locked chests showing "In use") keep their always-on behavior.
			bool hideLocked = HideWhenLocked && _interactable != null && !_interactable.CanInteract;

			if (dist > VisibilityRange || gated || hideLocked)
			{
				if (_canvasGO.activeSelf) _canvasGO.SetActive(false);
				return;
			}

			if (!_canvasGO.activeSelf) _canvasGO.SetActive(true);

			var anchor = transform.position + transform.TransformVector(LocalOffset);
			if (CameraBias > 0f)
			{
				Vector3 toCamera = _cameraTransform.position - anchor;
				float toCameraDist = toCamera.magnitude;
				if (toCameraDist > 0.0001f)
				{
					anchor += toCamera * (Mathf.Min(CameraBias, toCameraDist * 0.5f) / toCameraDist);
				}
			}
			_canvasGO.transform.position = anchor;
			_canvasGO.transform.rotation = _cameraTransform.rotation;
			// Canvas is unparented — localScale == worldScale, so a flat uniform value is correct.
			_canvasGO.transform.localScale = Vector3.one * CanvasScale;

			// Highlight only the prompt currently chosen by the scanner.
			bool selected = InteractionScanner.CurrentTarget == transform;
			_box.color = selected ? ActiveColor : InactiveColor;

			bool holding = InteractionScanner.HoldingTarget == transform;
			float fill = holding ? InteractionScanner.HoldProgress : 0f;
			if (_holdFill.fillAmount != fill) _holdFill.fillAmount = fill;
			if (_holdFill.enabled != holding) _holdFill.enabled = holding;
		}

		private void BuildCanvas()
		{
			// Detached from the host transform so the host's scale (e.g. Bookcase 1×2×0.5)
			// can never reach the indicator. Position/rotation are driven manually each
			// LateUpdate; we destroy the canvas in OnDestroy below.
			_canvasGO = new GameObject("InteractionPrompt");
			_canvasGO.transform.SetParent(null, false);
			_canvasGO.transform.position = transform.position;
			_canvasGO.transform.localScale = Vector3.one * CanvasScale;

			var canvas = _canvasGO.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.WorldSpace;
			canvas.overrideSorting = true;
			canvas.sortingOrder = CanvasSortOrder;

			var rt = _canvasGO.GetComponent<RectTransform>();
			rt.sizeDelta = IndicatorSize;

			var boxGO = new GameObject("Box", typeof(RectTransform), typeof(Image));
			boxGO.transform.SetParent(_canvasGO.transform, false);
			var brt = (RectTransform)boxGO.transform;
			brt.anchorMin = Vector2.zero;
			brt.anchorMax = Vector2.one;
			brt.offsetMin = Vector2.zero;
			brt.offsetMax = Vector2.zero;

			_box = boxGO.GetComponent<Image>();
			_box.color = InactiveColor;
			_box.raycastTarget = false;

			var overlay = GetOverlayMaterial();
			if (overlay != null) _box.material = overlay;

			// Radial fill drawn on top of the box while the player is holding Interact to pick this up.
			var fillGO = new GameObject("HoldFill", typeof(RectTransform), typeof(Image));
			fillGO.transform.SetParent(_canvasGO.transform, false);
			var frt = (RectTransform)fillGO.transform;
			frt.anchorMin = Vector2.zero;
			frt.anchorMax = Vector2.one;
			frt.offsetMin = Vector2.zero;
			frt.offsetMax = Vector2.zero;

			_holdFill = fillGO.GetComponent<Image>();
			_holdFill.color = HoldColor;
			_holdFill.raycastTarget = false;
			_holdFill.type = Image.Type.Filled;
			_holdFill.fillMethod = Image.FillMethod.Radial360;
			_holdFill.fillOrigin = (int)Image.Origin360.Top;
			_holdFill.fillClockwise = true;
			_holdFill.fillAmount = 0f;
			_holdFill.enabled = false;
			if (overlay != null) _holdFill.material = overlay;
		}
	}
}
