using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Starter
{
	/// <summary>
	/// Component that handle showing nicknames above player, plus a small relation indicator (a coloured circle)
	/// next to the name: blue = neutral, green = friendly (ally), red = hostile. The relationship itself is decided
	/// by the gameplay layer (see <c>Player</c> in Starter.Shooter) and pushed in via <see cref="SetRelation"/> —
	/// this component only owns the presentation (which colour maps to which relation).
	/// </summary>
	public class UINameplate : MonoBehaviour
	{
		public enum NameplateRelation { Neutral, Friendly, Hostile }

		public TextMeshProUGUI NicknameText;

		[Header("Relation indicator")]
		[Tooltip("Diameter of the relation circle, in the nameplate canvas' world units.")]
		public float IconSize = 0.16f;
		[Tooltip("Colour shown for non-ally players outside the Purge (Day / Lobby).")]
		public Color NeutralColor = new Color(0.30f, 0.55f, 1f);   // blue
		[Tooltip("Colour shown for team-mates (allies).")]
		public Color FriendlyColor = new Color(0.30f, 0.90f, 0.40f); // green
		[Tooltip("Colour shown for non-ally players once the Purge (Night) begins.")]
		public Color HostileColor = new Color(1f, 0.25f, 0.25f);     // red

		private Transform _cameraTransform;
		private Image _relationIcon;
		private NameplateRelation _relation = NameplateRelation.Neutral;
		private bool _relationApplied;

		public void SetNickname(string nickname)
		{
			if (NicknameText != null)
				NicknameText.text = nickname;

			PositionRelationIcon();
		}

		/// <summary>Sets the relation circle's colour. Cheap to call every frame — it only touches the image when the
		/// relation actually changes.</summary>
		public void SetRelation(NameplateRelation relation)
		{
			if (_relationApplied && _relation == relation)
				return;

			_relation = relation;
			_relationApplied = true;

			if (_relationIcon != null)
				_relationIcon.color = ColorFor(relation);
		}

		private void Awake()
		{
			// NOTE: do NOT cache Camera.main here. The nameplate GameObject is activated at runtime (the moment a
			// remote Player learns its nickname), and Camera.main can momentarily be null at that point — dereferencing
			// it would throw straight out of Player.Spawned. Resolve the camera lazily in LateUpdate instead.
			if (NicknameText != null)
				NicknameText.text = string.Empty;

			EnsureRelationIcon();
		}

		private void LateUpdate()
		{
			// Rotate nameplate toward camera. Resolve the camera lazily and tolerate it being absent for a frame.
			if (_cameraTransform == null)
			{
				var cam = Camera.main;
				if (cam == null)
					return;
				_cameraTransform = cam.transform;
			}

			transform.rotation = _cameraTransform.rotation;
		}

		private Color ColorFor(NameplateRelation relation)
		{
			switch (relation)
			{
				case NameplateRelation.Friendly: return FriendlyColor;
				case NameplateRelation.Hostile:  return HostileColor;
				default:                         return NeutralColor;
			}
		}

		// Builds the relation circle in code rather than on the prefab — keeps the indicator entirely in this shared
		// component (no per-prefab wiring) and avoids touching the Player prefab's Fusion bake.
		private void EnsureRelationIcon()
		{
			if (_relationIcon != null)
				return;

			// Parent under the same canvas the nickname lives on, so the circle shares its world-space scale.
			var parent = NicknameText != null ? NicknameText.transform.parent as RectTransform : transform as RectTransform;
			if (parent == null)
				return;

			var go = new GameObject("RelationIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			go.layer = gameObject.layer;

			var rt = (RectTransform)go.transform;
			rt.SetParent(parent, false);
			// Anchored to the canvas centre (where the centre-aligned name sits); PositionRelationIcon nudges it to
			// just left of the rendered text once the nickname is known.
			rt.anchorMin = new Vector2(0.5f, 0.5f);
			rt.anchorMax = new Vector2(0.5f, 0.5f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.sizeDelta = new Vector2(IconSize, IconSize);

			_relationIcon = go.GetComponent<Image>();
			_relationIcon.sprite = GetCircleSprite();
			_relationIcon.raycastTarget = false;
			_relationIcon.color = ColorFor(_relation);

			PositionRelationIcon();
		}

		// Places the circle just to the left of the (centre-aligned) name text, with a small gap. The name fills the
		// canvas, so we offset from centre by half the rendered text width rather than pinning to a fixed edge.
		private void PositionRelationIcon()
		{
			if (_relationIcon == null || NicknameText == null)
				return;

			NicknameText.ForceMeshUpdate();
			float halfText = NicknameText.preferredWidth * 0.5f;
			float gap = IconSize * 0.35f;

			var rt = (RectTransform)_relationIcon.transform;
			rt.anchoredPosition = new Vector2(-(halfText + gap + IconSize * 0.5f), 0f);
		}

		// One white circle texture shared by every nameplate (tinted per-relation via Image.color). Generated in code
		// so it needs no asset / Resources load.
		private static Sprite _circleSprite;

		private static Sprite GetCircleSprite()
		{
			if (_circleSprite != null)
				return _circleSprite;

			const int size = 64;
			float radius = size * 0.5f;

			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
			var pixels = new Color32[size * size];
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float dx = x + 0.5f - radius;
					float dy = y + 0.5f - radius;
					float dist = Mathf.Sqrt(dx * dx + dy * dy);
					// 1px-wide antialiased edge: fully opaque inside, fading to transparent at the rim.
					float alpha = Mathf.Clamp01(radius - dist);
					pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
				}
			}
			tex.SetPixels32(pixels);
			tex.Apply();

			_circleSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
			return _circleSprite;
		}
	}
}
