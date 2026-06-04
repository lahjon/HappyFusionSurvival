using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.Shooter
{
	/// <summary>
	/// Main UI script for Shooter sample.
	/// </summary>
	public class UIShooter : MonoBehaviour
	{
		[Header("References")]
		public GameManager GameManager;
		public CanvasGroup CanvasGroup;
		public GameObject AliveGroup;
		public GameObject DeathGroup;
		public Image HealthBar;
		public float HealthLerpSpeed = 6f;
		public Image StaminaBar;
		public float StaminaLerpSpeed = 6f;
		public TextMeshProUGUI DayTimeLabel;

		[Header("Money (top-right)")]
		[Tooltip("Wrapper for the money readout; hidden while there is no local player. Optional — leave null if you only need the label.")]
		public GameObject MoneyGroup;
		[Tooltip("Label that displays the local player's Money value. Updated every frame.")]
		public TextMeshProUGUI MoneyLabel;

		[Header("Downed Overlay")]
		[Tooltip("Wrapper shown only while the local player is downed (bleeding out).")]
		public GameObject DownedGroup;
		[Tooltip("Big countdown text — formatted to whole seconds.")]
		public TextMeshProUGUI DownedTimerLabel;
		[Tooltip("Fills 0..1 from drained to full as the bleed-out timer counts down. Optional — leave null to hide.")]
		public Image DownedBleedBar;
		[Tooltip("Fills 0..1 with ReviveProgress while at least one ally is holding revive on the local player. Optional.")]
		public Image DownedReviveBar;

		[Header("Match Event Banner (auto-built at runtime from DayTimeLabel)")]
		[Tooltip("Colour for events that directly threaten the local player (you're the hunter / standing in the fire).")]
		public Color EventUrgentColor = new Color(1f, 0.25f, 0.2f);
		[Tooltip("Colour for general event warnings (a hunter is loose / blackout).")]
		public Color EventWarnColor = new Color(1f, 0.72f, 0.12f);
		[Tooltip("Top-centre anchored offset of the banner, in canvas units.")]
		public Vector2 EventBannerOffset = new Vector2(0f, -90f);
		[Tooltip("Banner font size relative to DayTimeLabel.")]
		public float EventBannerFontScale = 1.4f;

		[Header("Elimination Banner (auto-built at runtime from DayTimeLabel)")]
		[Tooltip("Colour of the big centre-screen \"Eliminated <name>\" confirmation.")]
		public Color EliminationColor = new Color(1f, 0.25f, 0.2f);
		[Tooltip("Banner font size relative to DayTimeLabel — large, centre-screen kill confirm.")]
		public float EliminationFontScale = 2.2f;
		[Tooltip("Centre-anchored offset of the elimination banner, in canvas units (above the crosshair).")]
		public Vector2 EliminationOffset = new Vector2(0f, 140f);
		[Tooltip("Seconds the banner stays fully opaque before it begins to fade.")]
		public float EliminationHoldSeconds = 0.6f;
		[Tooltip("Seconds the banner takes to fade from opaque to gone.")]
		public float EliminationFadeSeconds = 0.8f;

		[Header("Damage Vignette (auto-built at runtime — no scene wiring)")]
		[Tooltip("Colour of the red flash that pulses in from the screen edges when the local player takes damage.")]
		public Color DamageVignetteColor = new Color(0.75f, 0f, 0f, 1f);
		[Tooltip("Peak opacity of the vignette on a hit (0..1).")]
		[Range(0f, 1f)]
		public float DamageVignetteMaxAlpha = 0.7f;
		[Tooltip("How fast the vignette fades back out after a hit (higher = quicker).")]
		public float DamageVignetteFadeSpeed = 2.5f;

		[Header("UI Sound Setup")]
		public AudioSource AudioSource;
		public AudioClip HitReceivedClip;
		public AudioClip DeathClip;

		private int _lastMoney = -1;
		private int _lastHealth = -1;
		private float _displayedHealthFraction = 1f;
		private float _displayedStaminaFraction = 1f;
		private bool _wasAlive = true;
		private TextMeshProUGUI _eventLabel;
		private TextMeshProUGUI _armingLabel;
		private TextMeshProUGUI _ammoLabel;
		private TextMeshProUGUI _eliminationLabel;
		private TextMeshProUGUI _duskLabel;
		private bool _duskWarningActive;
		private string _duskMessage;
		private Image _damageVignette;
		private float _damageVignetteAlpha;

		[Header("Ammo Readout (auto-built at runtime from DayTimeLabel)")]
		[Tooltip("Anchored offset of the magazine/reserve readout, relative to screen centre (near the crosshair).")]
		public Vector2 AmmoReadoutOffset = new Vector2(150f, -70f);
		[Tooltip("Colour of the ammo readout.")]
		public Color AmmoReadoutColor = Color.white;
		[Tooltip("Colour of the ammo readout while reloading.")]
		public Color AmmoReloadingColor = new Color(1f, 0.72f, 0.12f);

		[Header("PvP Arming Prompt (auto-built at runtime from DayTimeLabel)")]
		[Tooltip("Shown during Night while the local player has not yet reached their team's zone (cannot fire).")]
		public string ArmingPromptText = "REACH YOUR ZONE TO ARM";
		[Tooltip("Colour of the arming prompt.")]
		public Color ArmingPromptColor = new Color(1f, 0.72f, 0.12f);
		[Tooltip("Centre-anchored offset of the arming prompt, in canvas units.")]
		public Vector2 ArmingPromptOffset = new Vector2(0f, -120f);

		[Header("Grapple Reticle (auto-built at runtime — no scene wiring)")]
		[Tooltip("Ring colour when the aimed surface is in grapple range and a charge is available (can attach).")]
		public Color GrappleReticleValidColor = new Color(0.55f, 1f, 0.6f, 0.95f);
		[Tooltip("Ring colour when out of range or out of charges (can't attach).")]
		public Color GrappleReticleInvalidColor = new Color(1f, 1f, 1f, 0.3f);
		[Tooltip("Ring diameter (canvas px) when a valid anchor is in range — the reticle grows to this.")]
		public float GrappleReticleValidSize = 92f;
		[Tooltip("Ring diameter (canvas px) when out of range — the resting/small size.")]
		public float GrappleReticleInvalidSize = 46f;
		[Tooltip("How fast the ring eases between the small/large + colour states (higher = snappier).")]
		public float GrappleReticleLerpSpeed = 14f;

		private RectTransform _grappleReticle;
		private Image _grappleReticleImg;
		private float _grappleReticleSize;

		[Header("Dusk Warning Banner (auto-built at runtime from DayTimeLabel)")]
		[Tooltip("Shown when the town siren sounds at DuskWarning — the Purge is about to begin.")]
		public Color DuskWarningColor = new Color(1f, 0.35f, 0.15f);
		[Tooltip("Banner font size relative to DayTimeLabel.")]
		public float DuskBannerFontScale = 1.5f;
		[Tooltip("Top-centre anchored offset of the dusk banner, in canvas units.")]
		public Vector2 DuskBannerOffset = new Vector2(0f, -130f);
		[Tooltip("Speed of the attention-grabbing alpha pulse (0 = no pulse).")]
		public float DuskBannerPulseSpeed = 3f;

		private void Update()
		{
			// Fade the red damage vignette back out (triggered below when health drops).
			if (_damageVignette != null)
			{
				_damageVignetteAlpha = Mathf.MoveTowards(_damageVignetteAlpha, 0f, Time.deltaTime * DamageVignetteFadeSpeed);
				var vc = DamageVignetteColor;
				vc.a = _damageVignetteAlpha;
				_damageVignette.color = vc;
			}

			UpdateDayTimeLabel();
			UpdateEventBanner();
			UpdateArmingPrompt();
			UpdateDuskWarningBanner();

			var player = GameManager.LocalPlayer;
			UpdateAmmoReadout(player);
			UpdateGrappleReticle(player);
			UpdateEliminationBanner(player);
			if (player == null)
			{
				CanvasGroup.alpha = 0f;
				if (MoneyGroup != null && MoneyGroup.activeSelf) MoneyGroup.SetActive(false);
				return;
			}

			CanvasGroup.alpha = 1f;

			if (MoneyGroup != null && MoneyGroup.activeSelf == false) MoneyGroup.SetActive(true);
			if (MoneyLabel != null && _lastMoney != player.Money)
			{
				_lastMoney = player.Money;
				MoneyLabel.text = $"${_lastMoney}";
			}

			if (_lastHealth != player.Health.CurrentHealth)
			{
				bool isAlive = player.Health.IsAlive;

				if (_lastHealth > player.Health.CurrentHealth)
				{
					// Show hit received
					_damageVignetteAlpha = DamageVignetteMaxAlpha;

					var clip = isAlive ? HitReceivedClip : DeathClip;
					AudioSource.PlayOneShot(clip);
				}

				_lastHealth = player.Health.CurrentHealth;

				AliveGroup.SetActive(isAlive);
				DeathGroup.SetActive(isAlive == false);
			}

			// Downed overlay: visible only while the local player is downed. IsAlive stays true
			// during the bleed-out (Health is clamped to 1), so AliveGroup remains on too — the
			// downed group is rendered on top as an emphasis overlay.
			bool isDowned = player.IsDowned;
			if (DownedGroup != null && DownedGroup.activeSelf != isDowned)
			{
				DownedGroup.SetActive(isDowned);
			}
			if (isDowned)
			{
				if (DownedTimerLabel != null)
				{
					int secs = Mathf.CeilToInt(Mathf.Max(0f, player.DownedTimeRemaining));
					DownedTimerLabel.text = secs.ToString();
				}
				if (DownedBleedBar != null && player.DownedBleedOutSeconds > 0f)
				{
					DownedBleedBar.fillAmount = Mathf.Clamp01(player.DownedTimeRemaining / player.DownedBleedOutSeconds);
				}
				if (DownedReviveBar != null)
				{
					DownedReviveBar.fillAmount = Mathf.Clamp01(player.ReviveProgress);
				}
			}

			// Snap (rather than sweep) the bars on respawn (dead → alive); shared by both bars below.
			bool respawned = player.Health.IsAlive && _wasAlive == false;

			if (HealthBar != null && player.Health.InitialHealth > 0)
			{
				float target = Mathf.Clamp01((float)player.Health.CurrentHealth / player.Health.InitialHealth);

				if (respawned)
					_displayedHealthFraction = target;

				_displayedHealthFraction = Mathf.Lerp(_displayedHealthFraction, target, Time.deltaTime * HealthLerpSpeed);
				HealthBar.fillAmount = _displayedHealthFraction;
			}

			if (StaminaBar != null && player.MaxStamina > 0f)
			{
				float target = Mathf.Clamp01(player.Stamina / player.MaxStamina);

				if (respawned)
					_displayedStaminaFraction = target;

				_displayedStaminaFraction = Mathf.Lerp(_displayedStaminaFraction, target, Time.deltaTime * StaminaLerpSpeed);
				StaminaBar.fillAmount = _displayedStaminaFraction;
			}

			_wasAlive = player.Health.IsAlive;
		}

		private void Awake()
		{
			BuildEventBanner();
			BuildArmingPrompt();
			BuildAmmoReadout();
			BuildEliminationBanner();
			BuildDamageVignette();
			BuildDuskWarningBanner();
			BuildGrappleReticle();
		}

		private void OnEnable()
		{
			WorldSiren.WarningRaised  += OnDuskWarningRaised;
			WorldSiren.WarningCleared += OnDuskWarningCleared;
		}

		private void OnDisable()
		{
			WorldSiren.WarningRaised  -= OnDuskWarningRaised;
			WorldSiren.WarningCleared -= OnDuskWarningCleared;
		}

		// Build a full-screen red vignette overlay at runtime — no bespoke prefab or scene wiring, same
		// trick as the event/ammo banners. The sprite is a radial gradient (clear centre → opaque edge),
		// so the centre/crosshair stays readable and only the screen borders flush red on a hit.
		private void BuildDamageVignette()
		{
			var canvas = CanvasGroup != null ? CanvasGroup.GetComponentInParent<Canvas>()
				: (DayTimeLabel != null ? DayTimeLabel.GetComponentInParent<Canvas>() : null);
			if (canvas == null) return;

			var go = new GameObject("DamageVignette", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			var rt = (RectTransform)go.transform;
			rt.SetParent(canvas.transform, false);
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;
			rt.SetAsLastSibling(); // on top of the HUD; the clear centre keeps the crosshair/readouts visible

			_damageVignette = go.GetComponent<Image>();
			_damageVignette.raycastTarget = false;
			_damageVignette.sprite = BuildVignetteSprite();
			var c = DamageVignetteColor;
			c.a = 0f;
			_damageVignette.color = c;
		}

		// Generate the radial-gradient sprite used by the damage vignette: white RGB (tinted red by the
		// Image colour) with alpha ramping from 0 at the centre to 1 at the edges/corners.
		private static Sprite BuildVignetteSprite()
		{
			const int size = 128;
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
			{
				wrapMode   = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
				name       = "DamageVignetteTex",
			};

			var pixels = new Color32[size * size];
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float dx = (x / (float)(size - 1)) - 0.5f;
					float dy = (y / (float)(size - 1)) - 0.5f;
					float d  = Mathf.Sqrt(dx * dx + dy * dy) * 2f; // 0 centre → ~1 edge → ~1.41 corner
					float a  = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.1f, d));
					pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
				}
			}

			tex.SetPixels32(pixels);
			tex.Apply();

			return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
		}

		// Build the centre-screen grapple reticle at runtime — a ring that grows + turns the valid colour when
		// the player is aiming at a surface within reel range, so they can see whether a press will attach.
		// Same no-scene-wiring trick as the vignette; hidden unless a grapple gadget is held.
		private void BuildGrappleReticle()
		{
			var canvas = CanvasGroup != null ? CanvasGroup.GetComponentInParent<Canvas>()
				: (DayTimeLabel != null ? DayTimeLabel.GetComponentInParent<Canvas>() : null);
			if (canvas == null) return;

			var go = new GameObject("GrappleReticle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			var rt = (RectTransform)go.transform;
			rt.SetParent(canvas.transform, false);
			rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.anchoredPosition = Vector2.zero;
			rt.sizeDelta = new Vector2(GrappleReticleInvalidSize, GrappleReticleInvalidSize);

			_grappleReticle = rt;
			_grappleReticleImg = go.GetComponent<Image>();
			_grappleReticleImg.raycastTarget = false;
			_grappleReticleImg.sprite = BuildRingSprite();
			_grappleReticleImg.color = GrappleReticleInvalidColor;
			_grappleReticleSize = GrappleReticleInvalidSize;
			go.SetActive(false);
		}

		// Show the reticle whenever a grapple is held; grow it + fade it to the valid colour when the aimed
		// surface is in range (charge available), shrink + dim when not. Eased so the "snap" reads clearly.
		private void UpdateGrappleReticle(Player player)
		{
			if (_grappleReticle == null) return;

			bool holding = player != null && player.IsHoldingGrapple
				&& player.Health != null && player.Health.IsAlive;

			if (holding)
			{
				bool valid = player.IsGrappleTargetValid();
				float targetSize  = valid ? GrappleReticleValidSize : GrappleReticleInvalidSize;
				Color targetColor = valid ? GrappleReticleValidColor : GrappleReticleInvalidColor;

				float t = Time.deltaTime * GrappleReticleLerpSpeed;
				_grappleReticleSize = Mathf.Lerp(_grappleReticleSize, targetSize, t);
				_grappleReticle.sizeDelta = new Vector2(_grappleReticleSize, _grappleReticleSize);
				_grappleReticleImg.color = Color.Lerp(_grappleReticleImg.color, targetColor, t);
			}

			if (_grappleReticle.gameObject.activeSelf != holding)
				_grappleReticle.gameObject.SetActive(holding);
		}

		// Soft-edged ring (annulus) sprite for the grapple reticle: white RGB tinted by the Image colour,
		// alpha 1 in the ring band and 0 inside/outside, with a couple-pixel feather so it doesn't alias.
		private static Sprite BuildRingSprite()
		{
			const int size = 128;
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
			{
				wrapMode   = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
				name       = "GrappleReticleTex",
			};

			var pixels = new Color32[size * size];
			float center = (size - 1) * 0.5f;
			float outer  = size * 0.5f * 0.95f;
			float inner  = size * 0.5f * 0.66f;
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float dx = x - center, dy = y - center;
					float r  = Mathf.Sqrt(dx * dx + dy * dy);
					float a  = (r < inner || r > outer) ? 0f : Mathf.Clamp01(Mathf.Min(r - inner, outer - r) / 2.5f);
					pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
				}
			}

			tex.SetPixels32(pixels);
			tex.Apply();

			return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
		}

		// Clone the day/night label to make a big, centre-screen kill-confirmation banner — same
		// no-bespoke-prefab trick as the event banner. Driven by the local player's networked-RPC-fed
		// LastEliminatedName / LastEliminationTime stamps.
		private void BuildEliminationBanner()
		{
			if (DayTimeLabel == null) return;

			_eliminationLabel = Instantiate(DayTimeLabel, DayTimeLabel.transform.parent);
			_eliminationLabel.name = "EliminationBanner";

			var rt = _eliminationLabel.rectTransform;
			rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.anchoredPosition = EliminationOffset;

			_eliminationLabel.fontSize = DayTimeLabel.fontSize * EliminationFontScale;
			_eliminationLabel.alignment = TextAlignmentOptions.Center;
			_eliminationLabel.fontStyle |= FontStyles.Bold;
			_eliminationLabel.color = EliminationColor;
			_eliminationLabel.gameObject.SetActive(false);
		}

		// Flash "Eliminated <name>" centre-screen when the local player gets a kill, then fade out fast.
		// The kill event arrives via Player.RPC_NotifyElimination (host → killer); here we only render the
		// hold-then-fade from the local timestamp. Hidden when there's nothing recent to show.
		private void UpdateEliminationBanner(Player player)
		{
			if (_eliminationLabel == null) return;

			float elapsed = player != null ? Time.unscaledTime - player.LastEliminationTime : 999f;
			bool show = player != null
				&& string.IsNullOrEmpty(player.LastEliminatedName) == false
				&& elapsed >= 0f
				&& elapsed < EliminationHoldSeconds + EliminationFadeSeconds;

			if (show)
			{
				float alpha = elapsed < EliminationHoldSeconds
					? 1f
					: 1f - Mathf.Clamp01((elapsed - EliminationHoldSeconds) / Mathf.Max(0.01f, EliminationFadeSeconds));

				var color = EliminationColor;
				color.a = alpha;
				_eliminationLabel.color = color;
				_eliminationLabel.text = $"Eliminated {player.LastEliminatedName}";
			}

			if (_eliminationLabel.gameObject.activeSelf != show)
				_eliminationLabel.gameObject.SetActive(show);
		}

		// Clone the day/night label to make a crosshair-adjacent ammo readout — no bespoke prefab needed,
		// same trick as the event banner / arming prompt.
		private void BuildAmmoReadout()
		{
			if (DayTimeLabel == null) return;

			_ammoLabel = Instantiate(DayTimeLabel, DayTimeLabel.transform.parent);
			_ammoLabel.name = "AmmoReadout";

			var rt = _ammoLabel.rectTransform;
			rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.anchoredPosition = AmmoReadoutOffset;

			_ammoLabel.alignment = TextAlignmentOptions.Left;
			_ammoLabel.fontStyle |= FontStyles.Bold;
			_ammoLabel.color = AmmoReadoutColor;
			_ammoLabel.gameObject.SetActive(false);
		}

		// Held magazine weapon → "loaded / reserve" (or "RELOADING"); held charge gadget (grapple) →
		// "charges / max" (orange once spent). Hidden otherwise. Reads the networked Inventory each frame —
		// no [Networked] state on the UI.
		private void UpdateAmmoReadout(Player player)
		{
			if (_ammoLabel == null) return;

			var inventory = player != null ? player.GetComponent<Inventory>() : null;
			bool alive    = player != null && player.Health != null && player.Health.IsAlive;
			bool magazine = inventory != null && inventory.ActiveUsesMagazine;
			bool charges  = inventory != null && inventory.ActiveUsesCharges;
			bool show      = (magazine || charges) && alive;

			if (show && magazine)
			{
				if (inventory.IsReloading)
				{
					_ammoLabel.color = AmmoReloadingColor;
					_ammoLabel.text = "RELOADING";
				}
				else
				{
					_ammoLabel.color = AmmoReadoutColor;
					_ammoLabel.text = $"{inventory.ActiveLoaded} / {inventory.ActiveReserve}";
				}
			}
			else if (show) // charge gadget
			{
				int max = inventory.ActiveGadget != null ? inventory.ActiveGadget.MaxCharges : 0;
				_ammoLabel.color = inventory.ActiveCharges > 0 ? AmmoReadoutColor : AmmoReloadingColor;
				_ammoLabel.text = $"{inventory.ActiveCharges} / {max}";
			}

			if (_ammoLabel.gameObject.activeSelf != show)
				_ammoLabel.gameObject.SetActive(show);
		}

		// Clone of the day/night label, centred, used to tell the local player they must reach their team
		// zone before their weapons arm at Night. Same no-bespoke-prefab trick as BuildEventBanner.
		private void BuildArmingPrompt()
		{
			if (DayTimeLabel == null) return;

			_armingLabel = Instantiate(DayTimeLabel, DayTimeLabel.transform.parent);
			_armingLabel.name = "PvpArmingPrompt";

			var rt = _armingLabel.rectTransform;
			rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.anchoredPosition = ArmingPromptOffset;

			_armingLabel.alignment = TextAlignmentOptions.Center;
			_armingLabel.fontStyle |= FontStyles.Bold;
			_armingLabel.color = ArmingPromptColor;
			_armingLabel.text = ArmingPromptText;
			_armingLabel.gameObject.SetActive(false);
		}

		// Show "reach your zone" only during Night, while alive, and before this player has armed.
		private void UpdateArmingPrompt()
		{
			if (_armingLabel == null) return;

			var match  = MatchManager.Instance;
			var player = GameManager != null ? GameManager.LocalPlayer : null;

			bool show = match != null
				&& match.Phase == MatchPhase.Night
				&& player != null
				&& player.Object != null
				&& player.Object.IsValid
				&& player.Health != null
				&& player.Health.IsAlive
				&& player.PvpArmed == false;

			if (_armingLabel.gameObject.activeSelf != show)
				_armingLabel.gameObject.SetActive(show);
		}

		// Clone the day/night label into a prominent top-centre alert shown while the dusk siren wails.
		// Driven entirely by WorldSiren's static events — no scene wiring, same trick as the other banners.
		private void BuildDuskWarningBanner()
		{
			if (DayTimeLabel == null) return;

			_duskLabel = Instantiate(DayTimeLabel, DayTimeLabel.transform.parent);
			_duskLabel.name = "DuskWarningBanner";

			var rt = _duskLabel.rectTransform;
			rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
			rt.pivot = new Vector2(0.5f, 1f);
			rt.anchoredPosition = DuskBannerOffset;

			_duskLabel.fontSize = DayTimeLabel.fontSize * DuskBannerFontScale;
			_duskLabel.alignment = TextAlignmentOptions.Top;
			_duskLabel.fontStyle |= FontStyles.Bold;
			_duskLabel.color = DuskWarningColor;
			_duskLabel.gameObject.SetActive(false);
		}

		private void OnDuskWarningRaised(string message, float secondsUntilNight)
		{
			_duskWarningActive = true;
			_duskMessage = message;
		}

		private void OnDuskWarningCleared()
		{
			_duskWarningActive = false;
		}

		// Show the dusk banner (message + live countdown) while the warning is active, pulsing for urgency.
		// The countdown reads MatchManager's phase timer so it ticks in real time; falls back to the raised
		// message alone if the match object isn't available. Self-heals if a WarningCleared was missed.
		private void UpdateDuskWarningBanner()
		{
			if (_duskLabel == null) return;

			var match = MatchManager.Instance;
			bool show = _duskWarningActive && (match == null || match.Phase == MatchPhase.DuskWarning);

			if (show)
			{
				string text = _duskMessage ?? string.Empty;
				if (match != null)
				{
					int secs = Mathf.CeilToInt(Mathf.Max(0f, match.RemainingPhaseSeconds));
					text = $"{_duskMessage}\n{secs / 60}:{secs % 60:D2}";
				}
				_duskLabel.text = text;

				float alpha = DuskBannerPulseSpeed > 0f
					? Mathf.Lerp(0.55f, 1f, Mathf.PingPong(Time.unscaledTime * DuskBannerPulseSpeed, 1f))
					: 1f;
				var color = DuskWarningColor;
				color.a = alpha;
				_duskLabel.color = color;
			}

			if (_duskLabel.gameObject.activeSelf != show)
				_duskLabel.gameObject.SetActive(show);
		}

		// Clone the (already-wired) day/night label to get a banner with matching font/canvas membership,
		// then reposition it top-centre. Avoids needing a bespoke prefab/scene object for the event HUD.
		private void BuildEventBanner()
		{
			if (DayTimeLabel == null) return;

			_eventLabel = Instantiate(DayTimeLabel, DayTimeLabel.transform.parent);
			_eventLabel.name = "MatchEventBanner";

			var rt = _eventLabel.rectTransform;
			rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
			rt.pivot = new Vector2(0.5f, 1f);
			rt.anchoredPosition = EventBannerOffset;

			_eventLabel.fontSize = DayTimeLabel.fontSize * EventBannerFontScale;
			_eventLabel.alignment = TextAlignmentOptions.Top;
			_eventLabel.fontStyle |= FontStyles.Bold;
			_eventLabel.gameObject.SetActive(false);
		}

		// Drives the runtime-built banner from GameHostManager's networked event slot. Per-event text/colour; the
		// banner is hidden whenever no event is active. New events get a branch here.
		private void UpdateEventBanner()
		{
			if (_eventLabel == null) return;

			var host   = GameHostManager.Instance;
			var player = GameManager != null ? GameManager.LocalPlayer : null;

			bool show = host != null
				&& host.ActiveEvent != MatchEventType.None
				&& player != null
				&& player.Object != null
				&& player.Object.IsValid;

			if (show)
			{
				int secs = Mathf.CeilToInt(Mathf.Max(0f, host.RemainingEventSeconds));
				string time = $"{secs / 60}:{secs % 60:D2}";

				switch (host.ActiveEvent)
				{
					case MatchEventType.Hunt:
					{
						var teams  = TeamManager.Instance;
						int myTeam = teams != null ? teams.TeamOf(player.Object.InputAuthority) : -1;
						if (host.IsEventTarget(myTeam))
						{
							_eventLabel.color = EventUrgentColor;
							_eventLabel.text  = $"YOU ARE THE HUNTER\nEliminate a team — {time}";
						}
						else
						{
							_eventLabel.color = EventWarnColor;
							_eventLabel.text  = $"A HUNTER IS LOOSE — {time}";
						}
						break;
					}

					case MatchEventType.Blackout:
						_eventLabel.color = EventWarnColor;
						_eventLabel.text  = $"⚡ BLACKOUT — the power is out — {time}";
						break;

					default:
						show = false;
						break;
				}
			}

			if (_eventLabel.gameObject.activeSelf != show)
				_eventLabel.gameObject.SetActive(show);
		}

		private void UpdateDayTimeLabel()
		{
			if (DayTimeLabel == null) return;

			var time = TimeManager.Instance;
			if (time == null)
			{
				DayTimeLabel.text = string.Empty;
				return;
			}

			int    secs  = Mathf.CeilToInt(time.PhaseRemaining);
			string phase = time.IsNight ? "NIGHT" : "DAY";
			DayTimeLabel.text = $"Day {time.CurrentDay} \u2014 {phase}  {secs / 60}:{secs % 60:D2}";
		}
	}
}
