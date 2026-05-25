using UnityEngine;
using UnityEngine.UI;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// Local-only camera-facing indicator that sits on any interactable
	/// (<see cref="PickupableItem"/>, <see cref="LootContainer"/>, ...). Renders a
	/// small box that fades in within <see cref="VisibilityRange"/> and brightens
	/// when the local camera is within <see cref="ActiveRange"/> (the action
	/// would actually trigger). Builds its own world-space canvas procedurally.
	/// </summary>
	public sealed class InteractionPrompt : MonoBehaviour
	{
		[Header("Layout")]
		public Vector3 LocalOffset = Vector3.zero;
		public float CanvasScale = 0.01f;
		public Vector2 IndicatorSize = new Vector2(60f, 60f);
		[Tooltip("Distance (m) to push the indicator toward the camera each frame so it isn't occluded by the host object's mesh.")]
		public float CameraBias = 0.4f;
		[Tooltip("Sorting order for the indicator's canvas. Raise above other UI to keep it visible.")]
		public int CanvasSortOrder = 100;

		[Header("Ranges (m)")]
		[Tooltip("Indicator becomes visible (in disabled state) when the local camera is within this distance.")]
		public float VisibilityRange = 2f;
		[Tooltip("Indicator brightens to the active state when the local camera is within this distance (= player's pickup range).")]
		public float ActiveRange = 2f;

		[Header("Colors")]
		public Color ActiveColor = new Color(0.3f, 0.65f, 1f, 1f);
		public Color InactiveColor = new Color(0.3f, 0.65f, 1f, 0.35f);

		private Transform _cameraTransform;
		private GameObject _canvasGO;
		private Image _box;

		private void Awake()
		{
			BuildCanvas();
			_canvasGO.SetActive(false);
		}

		private void LateUpdate()
		{
			if (_canvasGO == null) return;
			if (_cameraTransform == null && Camera.main != null)
				_cameraTransform = Camera.main.transform;
			if (_cameraTransform == null) return;

			float dist = Vector3.Distance(transform.position, _cameraTransform.position);

			if (dist > VisibilityRange)
			{
				if (_canvasGO.activeSelf) _canvasGO.SetActive(false);
				return;
			}

			if (!_canvasGO.activeSelf) _canvasGO.SetActive(true);

			var anchor = transform.position + transform.TransformVector(LocalOffset);
			Vector3 toCamera = _cameraTransform.position - anchor;
			float toCameraDist = toCamera.magnitude;
			if (toCameraDist > 0.0001f)
			{
				anchor += toCamera * (Mathf.Min(CameraBias, toCameraDist * 0.5f) / toCameraDist);
			}
			_canvasGO.transform.position = anchor;
			_canvasGO.transform.rotation = _cameraTransform.rotation;
			_box.color = (dist <= ActiveRange) ? ActiveColor : InactiveColor;
		}

		private void BuildCanvas()
		{
			_canvasGO = new GameObject("InteractionPrompt");
			_canvasGO.transform.SetParent(transform, false);
			_canvasGO.transform.localPosition = LocalOffset;
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
		}
	}
}
