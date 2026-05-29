using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only radar device. Lives on the Scanner's HandPrefab — Inventory instantiates one
	/// per player when the Scanner slot is selected, destroys it when deselected, so the
	/// "active only while held" rule falls out for free.
	///
	/// Builds the world-space radar UI procedurally (no sprite/material asset deps). The sweep
	/// line spins on every viewer's instance; only the input-authority's instance scans players
	/// and emits pings — remote viewers of someone else's device just see the sweep with no
	/// dots, which matches the "you only know what your own radar saw" gameplay rule.
	/// </summary>
	public sealed class HandScanner : MonoBehaviour
	{
		[Header("Scan")]
		[Tooltip("Horizontal radius (m) the radar covers. Targets outside are ignored.")]
		public float ScanRange = 10f;
		[Tooltip("Seconds for one full 360° sweep.")]
		public float SweepDuration = 10f;
		[Tooltip("Seconds a ping stays visible after being triggered (alpha lerps to 0 over this).")]
		public float PingLifetime = 3f;
		[Tooltip("If false, same-team players are filtered out (TeamManager.SameTeam). Default off — radar pings only show enemies.")]
		public bool IncludeTeammates = false;

		[Header("Visual")]
		[Tooltip("Local-space position of the radar canvas relative to the HandPrefab root.")]
		public Vector3 RadarLocalOffset = new(0f, 0.5f, 0.2f);
		[Tooltip("Local-space euler rotation of the radar disc (degrees). Default flips it 180° on Y so the front face points back at the camera.")]
		public Vector3 RadarLocalEuler = new(50f, 180f, 0f);
		[Tooltip("Uniform local scale of the radar canvas. The canvas is 256 logical pixels wide; final world size = 256 * scale * parent.lossyScale.")]
		public float RadarLocalScale = 0.004f;

		[Header("Colors")]
		public Color DiscColor = new(0f, 0.18f, 0f, 0.85f);
		public Color RingColor = new(0.4f, 1f, 0.4f, 0.9f);
		public Color SweepColor = new(0.4f, 1f, 0.4f, 0.9f);
		public Color SelfColor = new(0.85f, 1f, 0.85f, 1f);
		public Color PingColor = new(1f, 0.15f, 0.15f, 1f);

		// One static texture set shared by every Scanner instance — cheap and avoids reallocating
		// per held-equip. Generated once on the first Awake.
		private static Texture2D s_discTex;
		private static Texture2D s_ringTex;
		private static Texture2D s_sweepTex;
		private static Texture2D s_dotTex;

		private const int CanvasPx = 256;
		private const float RadarRadiusPx = 120f;

		private Player _ownerPlayer;
		private Inventory _ownerInventory;

		private RectTransform _root;
		private RectTransform _sweep;
		private RectTransform _pingParent;

		private float _sweepAngleDeg;
		private float _prevSweepAngleDeg;
		private float _nextPlayerRefreshTime;
		private readonly List<Player> _playerCache = new(16);

		// Per-ping pooled state. Indices align across the four lists.
		private readonly List<RectTransform> _pingRects = new(16);
		private readonly List<RawImage> _pingImages = new(16);
		private readonly List<float> _pingExpiry = new(16);
		private readonly List<bool> _pingAlive = new(16);

		private void Awake()
		{
			EnsureTextures();
			BuildCanvas();
			ResolveOwner();
		}

		// Walk up parents to find the Inventory that instantiated us. The HandPrefab lives under
		// HandAnchor → CameraHandle → CameraPivot → Player; Inventory is on the Player root.
		private void ResolveOwner()
		{
			var t = transform.parent;
			while (t != null)
			{
				if (t.TryGetComponent<Inventory>(out _ownerInventory))
				{
					_ownerPlayer = _ownerInventory.GetComponent<Player>();
					return;
				}
				t = t.parent;
			}
		}

		private static void EnsureTextures()
		{
			if (s_discTex == null) s_discTex = MakeDisc(128, 0.95f, -1f);   // solid filled disc
			if (s_ringTex == null) s_ringTex = MakeDisc(128, 0.98f, 0.90f); // hollow annulus = outer ring
			if (s_sweepTex == null) s_sweepTex = MakeSweep(64);
			if (s_dotTex == null) s_dotTex = MakeDisc(32, 0.95f, -1f);      // small solid dot
		}

		// Solid disc (ringStart < 0) or hollow annulus (ringStart >= 0) over [ringStart, outerR]
		// of the unit half-extent. White pixels with alpha set per-pixel — the consuming RawImage
		// tints via .color.
		private static Texture2D MakeDisc(int size, float outerR, float ringStart)
		{
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
			var pixels = new Color32[size * size];
			float center = (size - 1) * 0.5f;
			float maxR = size * 0.5f * outerR;
			float minR = ringStart >= 0f ? size * 0.5f * ringStart : -1f;
			for (int y = 0; y < size; y++)
			for (int x = 0; x < size; x++)
			{
				float dx = x - center, dy = y - center;
				float r = Mathf.Sqrt(dx * dx + dy * dy);
				bool inside = r <= maxR && (minR < 0f || r >= minR);
				pixels[y * size + x] = inside
					? new Color32(255, 255, 255, 255)
					: new Color32(255, 255, 255, 0);
			}
			tex.SetPixels32(pixels);
			tex.Apply(false, true);
			return tex;
		}

		// Vertical alpha ramp used as the sweep beam. Bright at the outer rim, fading toward the
		// hub — sells the "trailing scan line" look when rotated about the base pivot.
		private static Texture2D MakeSweep(int size)
		{
			var tex = new Texture2D(2, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
			var pixels = new Color32[2 * size];
			for (int y = 0; y < size; y++)
			{
				byte a = (byte)(y * 255 / Mathf.Max(1, size - 1));
				pixels[y * 2 + 0] = new Color32(255, 255, 255, a);
				pixels[y * 2 + 1] = new Color32(255, 255, 255, a);
			}
			tex.SetPixels32(pixels);
			tex.Apply(false, true);
			return tex;
		}

		private void BuildCanvas()
		{
			var canvasGO = new GameObject("ScannerRadarCanvas");
			canvasGO.transform.SetParent(transform, false);
			canvasGO.transform.localPosition = RadarLocalOffset;
			canvasGO.transform.localEulerAngles = RadarLocalEuler;
			canvasGO.transform.localScale = Vector3.one * RadarLocalScale;

			var canvas = canvasGO.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.WorldSpace;
			canvas.sortingOrder = 50;

			_root = canvasGO.GetComponent<RectTransform>();
			_root.sizeDelta = new Vector2(CanvasPx, CanvasPx);

			CreateRawImage("Disc", _root, s_discTex, DiscColor, new Vector2(RadarRadiusPx * 2f, RadarRadiusPx * 2f));
			CreateRawImage("Ring", _root, s_ringTex, RingColor, new Vector2(RadarRadiusPx * 2f, RadarRadiusPx * 2f));

			// Cardinal ticks at N/E/S/W on the rim.
			for (int i = 0; i < 4; i++)
			{
				float ang = i * 90f * Mathf.Deg2Rad;
				Vector2 p = new(Mathf.Sin(ang) * RadarRadiusPx, Mathf.Cos(ang) * RadarRadiusPx);
				var tick = CreateRawImage($"Tick{i}", _root, s_dotTex, RingColor, new Vector2(6f, 6f));
				tick.rectTransform.anchoredPosition = p;
			}

			// Sweep beam: a slim vertical strip pivoting from its base at the radar center.
			// Z-rotation = -sweepAngle so increasing angle reads as clockwise (matches our bearing math).
			var sweepGO = new GameObject("Sweep", typeof(RectTransform), typeof(RawImage));
			sweepGO.transform.SetParent(_root, false);
			_sweep = sweepGO.GetComponent<RectTransform>();
			_sweep.anchorMin = _sweep.anchorMax = new Vector2(0.5f, 0.5f);
			_sweep.pivot = new Vector2(0.5f, 0f);
			_sweep.sizeDelta = new Vector2(4f, RadarRadiusPx);
			_sweep.anchoredPosition = Vector2.zero;
			var sweepImg = sweepGO.GetComponent<RawImage>();
			sweepImg.texture = s_sweepTex;
			sweepImg.color = SweepColor;
			sweepImg.raycastTarget = false;

			CreateRawImage("Self", _root, s_dotTex, SelfColor, new Vector2(10f, 10f));

			var ppGO = new GameObject("Pings", typeof(RectTransform));
			ppGO.transform.SetParent(_root, false);
			_pingParent = ppGO.GetComponent<RectTransform>();
			_pingParent.anchorMin = _pingParent.anchorMax = new Vector2(0.5f, 0.5f);
			_pingParent.pivot = new Vector2(0.5f, 0.5f);
			_pingParent.sizeDelta = new Vector2(CanvasPx, CanvasPx);
		}

		private static RawImage CreateRawImage(string name, Transform parent, Texture2D tex, Color color, Vector2 size)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
			go.transform.SetParent(parent, false);
			var rect = go.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.sizeDelta = size;
			var img = go.GetComponent<RawImage>();
			img.texture = tex;
			img.color = color;
			img.raycastTarget = false;
			return img;
		}

		private void Update()
		{
			_prevSweepAngleDeg = _sweepAngleDeg;
			float deltaDeg = SweepDuration > 0f ? Time.deltaTime * 360f / SweepDuration : 0f;
			_sweepAngleDeg = (_sweepAngleDeg + deltaDeg) % 360f;
			if (_sweep != null) _sweep.localRotation = Quaternion.Euler(0f, 0f, -_sweepAngleDeg);

			FadePings();

			// Only the input-authority's instance scans for enemies. Remote viewers of someone
			// else's Scanner just see the sweep with no pings.
			if (_ownerPlayer == null || _ownerInventory == null || _ownerInventory.HasInputAuthority == false)
				return;

			if (Time.unscaledTime >= _nextPlayerRefreshTime)
			{
				RefreshPlayerCache();
				_nextPlayerRefreshTime = Time.unscaledTime + 1f;
			}

			ScanAndPing();
		}

		private void RefreshPlayerCache()
		{
			_playerCache.Clear();
			var all = FindObjectsByType<Player>(FindObjectsSortMode.None);
			for (int i = 0; i < all.Length; i++)
			{
				var p = all[i];
				if (p == null || p == _ownerPlayer) continue;
				_playerCache.Add(p);
			}
		}

		private void ScanAndPing()
		{
			Vector3 selfPos = _ownerPlayer.transform.position;
			Vector3 selfFwd = _ownerPlayer.transform.forward; selfFwd.y = 0f;
			if (selfFwd.sqrMagnitude < 1e-4f) selfFwd = Vector3.forward; else selfFwd.Normalize();
			// 90° clockwise from selfFwd in the horizontal plane.
			Vector3 selfRight = new(selfFwd.z, 0f, -selfFwd.x);

			var tm = TeamManager.Instance;
			var selfRef = _ownerPlayer.Object != null ? _ownerPlayer.Object.InputAuthority : default;

			for (int i = 0; i < _playerCache.Count; i++)
			{
				var p = _playerCache[i];
				if (p == null || p.Object == null || p.Object.IsValid == false) continue;
				if (p.Health != null && p.Health.IsAlive == false) continue;
				if (IncludeTeammates == false && tm != null && tm.SameTeam(selfRef, p.Object.InputAuthority)) continue;

				Vector3 delta = p.transform.position - selfPos;
				delta.y = 0f;
				float dist = delta.magnitude;
				if (dist > ScanRange || dist < 1e-4f) continue;

				// Bearing: 0° = directly forward, increasing clockwise to match the sweep direction.
				float bearing = Mathf.Atan2(Vector3.Dot(delta, selfRight), Vector3.Dot(delta, selfFwd)) * Mathf.Rad2Deg;
				if (bearing < 0f) bearing += 360f;

				if (SweepCrossed(_prevSweepAngleDeg, _sweepAngleDeg, bearing))
				{
					SpawnPing(bearing, dist);
				}
			}
		}

		// True iff the sweep advanced from prev to curr (always clockwise) and crossed `bearing`
		// on its way. Inclusive at curr so a player exactly aligned with the new front is caught.
		private static bool SweepCrossed(float prev, float curr, float bearing)
		{
			if (Mathf.Approximately(prev, curr)) return false;
			if (curr > prev) return bearing > prev && bearing <= curr;
			// Wrapped past 360°.
			return bearing > prev || bearing <= curr;
		}

		private void SpawnPing(float bearingDeg, float dist)
		{
			float radial = (dist / Mathf.Max(0.001f, ScanRange)) * RadarRadiusPx;
			float rad = bearingDeg * Mathf.Deg2Rad;
			Vector2 pos = new(Mathf.Sin(rad) * radial, Mathf.Cos(rad) * radial);

			int slot = -1;
			for (int i = 0; i < _pingAlive.Count; i++)
			{
				if (_pingAlive[i] == false) { slot = i; break; }
			}
			if (slot < 0)
			{
				var img = CreateRawImage($"Ping{_pingRects.Count}", _pingParent, s_dotTex, PingColor, new Vector2(12f, 12f));
				_pingRects.Add(img.rectTransform);
				_pingImages.Add(img);
				_pingExpiry.Add(0f);
				_pingAlive.Add(false);
				slot = _pingRects.Count - 1;
			}

			_pingRects[slot].anchoredPosition = pos;
			_pingImages[slot].color = PingColor;
			_pingImages[slot].gameObject.SetActive(true);
			_pingExpiry[slot] = Time.time + PingLifetime;
			_pingAlive[slot] = true;
		}

		private void FadePings()
		{
			float now = Time.time;
			for (int i = 0; i < _pingAlive.Count; i++)
			{
				if (_pingAlive[i] == false) continue;
				float remaining = _pingExpiry[i] - now;
				if (remaining <= 0f)
				{
					_pingAlive[i] = false;
					if (_pingImages[i] != null) _pingImages[i].gameObject.SetActive(false);
					continue;
				}
				if (_pingImages[i] != null)
				{
					Color c = PingColor;
					c.a *= Mathf.Clamp01(remaining / Mathf.Max(0.01f, PingLifetime));
					_pingImages[i].color = c;
				}
			}
		}
	}
}
