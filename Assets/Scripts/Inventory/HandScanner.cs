using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only radar device — the concrete <see cref="HeldGadget"/> for <see cref="RadarGadgetCapability"/>.
	/// Attached to the held hand instance at equip (see <c>Inventory.RefreshHeldItem</c>) and destroyed on
	/// deselect, so "active only while held" falls out for free.
	///
	/// Builds the world-space radar UI procedurally (no sprite/material asset deps). The sweep line spins on
	/// every viewer's instance (<see cref="OnRender"/>); only the input-authority's instance scans players and
	/// emits pings (<see cref="OnOwnerTick"/>) — remote viewers of someone else's device just see the sweep with
	/// no dots, which matches the "you only know what your own radar saw" gameplay rule.
	///
	/// All tuning is supplied by <see cref="RadarGadgetCapability"/> via <see cref="Initialize"/>; this component
	/// holds only per-instance runtime state.
	/// </summary>
	public sealed class HandScanner : HeldGadget
	{
		// Tuning copied from the RadarGadgetCapability on the item asset at equip (see Initialize).
		private float _scanRange = 10f;
		private float _sweepDuration = 10f;
		private float _pingLifetime = 3f;
		private bool _includeTeammates;

		private Vector3 _radarLocalOffset = new(0f, 0.5f, 0.2f);
		private Vector3 _radarLocalEuler = new(50f, 180f, 0f);
		private float _radarLocalScale = 0.004f;

		private Color _discColor = new(0f, 0.18f, 0f, 0.85f);
		private Color _ringColor = new(0.4f, 1f, 0.4f, 0.9f);
		private Color _sweepColor = new(0.4f, 1f, 0.4f, 0.9f);
		private Color _selfColor = new(0.85f, 1f, 0.85f, 1f);
		private Color _pingColor = new(1f, 0.15f, 0.15f, 1f);

		// One static texture set shared by every Scanner instance — cheap and avoids reallocating
		// per held-equip. Generated once on first activation.
		private static Texture2D s_discTex;
		private static Texture2D s_ringTex;
		private static Texture2D s_sweepTex;
		private static Texture2D s_dotTex;

		private const int CanvasPx = 256;
		private const float RadarRadiusPx = 120f;

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

		/// <summary>
		/// Seed this scanner from its authoring data and bring it online. Called by
		/// <see cref="RadarGadgetCapability.CreateRuntime"/> immediately after AddComponent, before the first
		/// Update — so the canvas is built with the final tuning, not the defaults.
		/// </summary>
		public void Initialize(RadarGadgetCapability cap)
		{
			if (cap != null)
			{
				_scanRange = cap.ScanRange;
				_sweepDuration = cap.SweepDuration;
				_pingLifetime = cap.PingLifetime;
				_includeTeammates = cap.IncludeTeammates;

				_radarLocalOffset = cap.RadarLocalOffset;
				_radarLocalEuler = cap.RadarLocalEuler;
				_radarLocalScale = cap.RadarLocalScale;

				_discColor = cap.DiscColor;
				_ringColor = cap.RingColor;
				_sweepColor = cap.SweepColor;
				_selfColor = cap.SelfColor;
				_pingColor = cap.PingColor;
			}

			Activate();
		}

		protected override void OnActivated()
		{
			EnsureTextures();
			BuildCanvas();
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
			canvasGO.transform.localPosition = _radarLocalOffset;
			canvasGO.transform.localEulerAngles = _radarLocalEuler;
			canvasGO.transform.localScale = Vector3.one * _radarLocalScale;

			var canvas = canvasGO.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.WorldSpace;
			canvas.sortingOrder = 50;

			_root = canvasGO.GetComponent<RectTransform>();
			_root.sizeDelta = new Vector2(CanvasPx, CanvasPx);

			CreateRawImage("Disc", _root, s_discTex, _discColor, new Vector2(RadarRadiusPx * 2f, RadarRadiusPx * 2f));
			CreateRawImage("Ring", _root, s_ringTex, _ringColor, new Vector2(RadarRadiusPx * 2f, RadarRadiusPx * 2f));

			// Cardinal ticks at N/E/S/W on the rim.
			for (int i = 0; i < 4; i++)
			{
				float ang = i * 90f * Mathf.Deg2Rad;
				Vector2 p = new(Mathf.Sin(ang) * RadarRadiusPx, Mathf.Cos(ang) * RadarRadiusPx);
				var tick = CreateRawImage($"Tick{i}", _root, s_dotTex, _ringColor, new Vector2(6f, 6f));
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
			sweepImg.color = _sweepColor;
			sweepImg.raycastTarget = false;

			CreateRawImage("Self", _root, s_dotTex, _selfColor, new Vector2(10f, 10f));

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

		protected override void OnRender()
		{
			_prevSweepAngleDeg = _sweepAngleDeg;
			float deltaDeg = _sweepDuration > 0f ? Time.deltaTime * 360f / _sweepDuration : 0f;
			_sweepAngleDeg = (_sweepAngleDeg + deltaDeg) % 360f;
			if (_sweep != null) _sweep.localRotation = Quaternion.Euler(0f, 0f, -_sweepAngleDeg);

			FadePings();
		}

		protected override void OnOwnerTick()
		{
			if (OwnerPlayer == null) return;

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
			var all = FindObjectsByType<Player>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
			for (int i = 0; i < all.Length; i++)
			{
				var p = all[i];
				if (p == null || p == OwnerPlayer) continue;
				_playerCache.Add(p);
			}
		}

		private void ScanAndPing()
		{
			Vector3 selfPos = OwnerPlayer.transform.position;
			Vector3 selfFwd = OwnerPlayer.transform.forward; selfFwd.y = 0f;
			if (selfFwd.sqrMagnitude < 1e-4f) selfFwd = Vector3.forward; else selfFwd.Normalize();
			// 90° clockwise from selfFwd in the horizontal plane.
			Vector3 selfRight = new(selfFwd.z, 0f, -selfFwd.x);

			var tm = TeamManager.Instance;
			var selfRef = OwnerPlayer.Object != null ? OwnerPlayer.Object.InputAuthority : default;

			for (int i = 0; i < _playerCache.Count; i++)
			{
				var p = _playerCache[i];
				if (p == null || p.Object == null || p.Object.IsValid == false) continue;
				if (p.Health != null && p.Health.IsAlive == false) continue;
				if (_includeTeammates == false && tm != null && tm.SameTeam(selfRef, p.Object.InputAuthority)) continue;

				Vector3 delta = p.transform.position - selfPos;
				delta.y = 0f;
				float dist = delta.magnitude;
				if (dist > _scanRange || dist < 1e-4f) continue;

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
			float radial = (dist / Mathf.Max(0.001f, _scanRange)) * RadarRadiusPx;
			float rad = bearingDeg * Mathf.Deg2Rad;
			Vector2 pos = new(Mathf.Sin(rad) * radial, Mathf.Cos(rad) * radial);

			int slot = -1;
			for (int i = 0; i < _pingAlive.Count; i++)
			{
				if (_pingAlive[i] == false) { slot = i; break; }
			}
			if (slot < 0)
			{
				var img = CreateRawImage($"Ping{_pingRects.Count}", _pingParent, s_dotTex, _pingColor, new Vector2(12f, 12f));
				_pingRects.Add(img.rectTransform);
				_pingImages.Add(img);
				_pingExpiry.Add(0f);
				_pingAlive.Add(false);
				slot = _pingRects.Count - 1;
			}

			_pingRects[slot].anchoredPosition = pos;
			_pingImages[slot].color = _pingColor;
			_pingImages[slot].gameObject.SetActive(true);
			_pingExpiry[slot] = Time.time + _pingLifetime;
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
					Color c = _pingColor;
					c.a *= Mathf.Clamp01(remaining / Mathf.Max(0.01f, _pingLifetime));
					_pingImages[i].color = c;
				}
			}
		}
	}
}
