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
		public Image HungerBar;
		public CanvasGroup HitIndicator;
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

		[Header("UI Sound Setup")]
		public AudioSource AudioSource;
		public AudioClip HitReceivedClip;
		public AudioClip DeathClip;

		private int _lastMoney = -1;
		private int _lastHealth = -1;
		private float _displayedHealthFraction = 1f;
		private bool _wasAlive = true;
		private TextMeshProUGUI _eventLabel;
		private TextMeshProUGUI _armingLabel;
		private TextMeshProUGUI _ammoLabel;
		private TextMeshProUGUI _eliminationLabel;

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

		private void Update()
		{
			// Fadeout hit indicator
			HitIndicator.alpha = Mathf.Lerp(HitIndicator.alpha, 0f, Time.deltaTime * 2f);

			UpdateDayTimeLabel();
			UpdateEventBanner();
			UpdateArmingPrompt();

			var player = GameManager.LocalPlayer;
			UpdateAmmoReadout(player);
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
					HitIndicator.alpha = 1f;

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

			if (HealthBar != null && player.Health.InitialHealth > 0)
			{
				float target = Mathf.Clamp01((float)player.Health.CurrentHealth / player.Health.InitialHealth);

				// Snap on respawn (dead → alive) so the bar doesn't sweep up from 0 noticeably.
				bool isAlive = player.Health.IsAlive;
				if (isAlive && _wasAlive == false)
					_displayedHealthFraction = target;
				_wasAlive = isAlive;

				_displayedHealthFraction = Mathf.Lerp(_displayedHealthFraction, target, Time.deltaTime * HealthLerpSpeed);
				HealthBar.fillAmount = _displayedHealthFraction;
			}

			if (StaminaBar != null && player.MaxStamina > 0f)
			{
				StaminaBar.fillAmount = Mathf.Clamp01(player.Stamina / player.MaxStamina);
			}

			// Hunger bar disabled in the Purge × Stardew pivot — the GameObject is hidden in Awake, so the
			// inspector reference stays valid but nothing renders. Restore the fill update if survival ever returns.
		}

		private void Awake()
		{
			// Hide the legacy hunger bar regardless of whether it's wired in the scene/prefab.
			if (HungerBar != null && HungerBar.gameObject != null)
			{
				HungerBar.gameObject.SetActive(false);
			}

			BuildEventBanner();
			BuildArmingPrompt();
			BuildAmmoReadout();
			BuildEliminationBanner();
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

		// Show "loaded / reserve" (or "RELOADING") for the held magazine-fed weapon; hidden otherwise.
		// Reads the networked Inventory each frame — no [Networked] state on the UI.
		private void UpdateAmmoReadout(Player player)
		{
			if (_ammoLabel == null) return;

			var inventory = player != null ? player.GetComponent<Inventory>() : null;
			bool show = inventory != null && inventory.ActiveUsesMagazine && player.Health != null && player.Health.IsAlive;

			if (show)
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
