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

		[Header("UI Sound Setup")]
		public AudioSource AudioSource;
		public AudioClip HitReceivedClip;
		public AudioClip DeathClip;

		private int _lastMoney = -1;
		private int _lastHealth = -1;
		private float _displayedHealthFraction = 1f;
		private bool _wasAlive = true;

		private void Update()
		{
			// Fadeout hit indicator
			HitIndicator.alpha = Mathf.Lerp(HitIndicator.alpha, 0f, Time.deltaTime * 2f);

			UpdateDayTimeLabel();

			var player = GameManager.LocalPlayer;
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
