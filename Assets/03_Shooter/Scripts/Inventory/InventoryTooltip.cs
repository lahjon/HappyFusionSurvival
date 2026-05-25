using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only tooltip widget that follows the cursor and renders an
	/// ItemDefinition's name, description, and stat rows. Built procedurally so
	/// the scene only needs a CanvasGroup-friendly parent.
	/// </summary>
	public sealed class InventoryTooltip : MonoBehaviour
	{
		public Vector2 CursorOffset = new Vector2(16f, -16f);
		public float Padding = 8f;
		public float MaxWidth = 280f;
		public Color BackgroundColor = new Color(0f, 0f, 0f, 0.85f);
		public Color TitleColor = new Color(1f, 0.85f, 0.1f, 1f);
		public Color BodyColor = Color.white;
		public Color StatColor = new Color(0.75f, 0.85f, 1f, 1f);

		private RectTransform _rt;
		private Canvas _canvas;
		private Image _background;
		private TMP_Text _title;
		private TMP_Text _body;
		private bool _visible;

		private void Awake()
		{
			Build();
			Hide();
		}

		private void LateUpdate()
		{
			if (!_visible)
				return;
			PositionAtCursor();
		}

		public void Show(ItemDefinition def, short count)
		{
			if (def == null)
			{
				Hide();
				return;
			}

			_title.text = string.IsNullOrEmpty(def.DisplayName) ? $"#{def.Id}" : def.DisplayName;
			_title.color = TitleColor;

			var sb = new StringBuilder(128);
			if (!string.IsNullOrEmpty(def.Description))
			{
				sb.Append(def.Description.Trim());
			}

			if (def.Stats != null && def.Stats.Length > 0)
			{
				if (sb.Length > 0) sb.Append("\n\n");
				for (int i = 0; i < def.Stats.Length; i++)
				{
					var stat = def.Stats[i];
					if (string.IsNullOrEmpty(stat.Label) && string.IsNullOrEmpty(stat.Value))
						continue;
					if (i > 0) sb.Append('\n');
					sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(StatColor)).Append('>');
					sb.Append(stat.Label);
					sb.Append("</color>: ");
					sb.Append(stat.Value);
				}
			}

			if (def.MaxStack > 1)
			{
				if (sb.Length > 0) sb.Append("\n\n");
				sb.Append("<size=85%>Stack: ").Append(count).Append(" / ").Append(def.MaxStack).Append("</size>");
			}

			_body.text = sb.ToString();
			_body.gameObject.SetActive(_body.text.Length > 0);

			_visible = true;
			gameObject.SetActive(true);
			LayoutRebuilder.ForceRebuildLayoutImmediate(_rt);
			PositionAtCursor();
		}

		public void Hide()
		{
			_visible = false;
			if (gameObject != null)
				gameObject.SetActive(false);
		}

		private void PositionAtCursor()
		{
			if (_canvas == null) return;

			Vector2 cursor = Input.mousePosition;
			Vector2 local;
			var parentRT = _rt.parent as RectTransform;
			if (parentRT == null) return;

			Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
			RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRT, cursor, cam, out local);

			Vector2 pos = local + CursorOffset;

			Vector2 size = _rt.sizeDelta;
			Rect parentRect = parentRT.rect;

			float halfW = size.x * 0.5f;
			float halfH = size.y * 0.5f;
			pos.x = Mathf.Clamp(pos.x, parentRect.xMin + halfW, parentRect.xMax - halfW);
			pos.y = Mathf.Clamp(pos.y, parentRect.yMin + halfH, parentRect.yMax - halfH);

			_rt.anchoredPosition = pos;
		}

		private void Build()
		{
			_canvas = GetComponentInParent<Canvas>();

			_rt = gameObject.GetComponent<RectTransform>();
			if (_rt == null) _rt = gameObject.AddComponent<RectTransform>();
			_rt.anchorMin = new Vector2(0.5f, 0.5f);
			_rt.anchorMax = new Vector2(0.5f, 0.5f);
			_rt.pivot = new Vector2(0f, 1f);
			_rt.sizeDelta = new Vector2(MaxWidth, 80f);

			_background = gameObject.GetComponent<Image>();
			if (_background == null) _background = gameObject.AddComponent<Image>();
			_background.color = BackgroundColor;
			_background.raycastTarget = false;

			var fitter = gameObject.GetComponent<ContentSizeFitter>();
			if (fitter == null) fitter = gameObject.AddComponent<ContentSizeFitter>();
			fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
			fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			var vlg = gameObject.GetComponent<VerticalLayoutGroup>();
			if (vlg == null) vlg = gameObject.AddComponent<VerticalLayoutGroup>();
			vlg.padding = new RectOffset((int)Padding, (int)Padding, (int)Padding, (int)Padding);
			vlg.spacing = 4f;
			vlg.childAlignment = TextAnchor.UpperLeft;
			vlg.childControlWidth = true;
			vlg.childControlHeight = true;
			vlg.childForceExpandWidth = false;
			vlg.childForceExpandHeight = false;

			_title = CreateText("Title", FontStyles.Bold, 16f, TitleColor);
			_body = CreateText("Body", FontStyles.Normal, 13f, BodyColor);
		}

		private TMP_Text CreateText(string name, FontStyles style, float size, Color color)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
			go.transform.SetParent(transform, false);
			var t = go.GetComponent<TextMeshProUGUI>();
			t.fontStyle = style;
			t.fontSize = size;
			t.color = color;
			t.raycastTarget = false;
			t.textWrappingMode = TextWrappingModes.Normal;
			t.alignment = TextAlignmentOptions.TopLeft;

			var le = go.AddComponent<LayoutElement>();
			le.preferredWidth = MaxWidth - Padding * 2f;
			return t;
		}
	}
}
