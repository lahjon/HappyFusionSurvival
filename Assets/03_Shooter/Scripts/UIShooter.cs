using Fusion;
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
		public TextMeshProUGUI ChickenCount;
		public TextMeshProUGUI BestHunter;
		public GameObject AliveGroup;
		public GameObject DeathGroup;
		public Image HealthBar;
		public float HealthLerpSpeed = 6f;
		public Image StaminaBar;
		public Image HungerBar;
		public CanvasGroup HitIndicator;
		public TextMeshProUGUI DayTimeLabel;

		[Header("UI Sound Setup")]
		public AudioSource AudioSource;
		public AudioClip ChickenKillClip;
		public AudioClip HitReceivedClip;
		public AudioClip DeathClip;

		private int _lastChickens = -1;
		private int _lastHealth = -1;
		private float _displayedHealthFraction = 1f;
		private bool _wasAlive = true;
		private PlayerRef _bestHunter;

		private void OnEnable()
		{
			BestHunter.gameObject.SetActive(false);
		}

		private void Update()
		{
			// Fadeout hit indicator
			HitIndicator.alpha = Mathf.Lerp(HitIndicator.alpha, 0f, Time.deltaTime * 2f);

			UpdateDayTimeLabel();

			var player = GameManager.LocalPlayer;
			if (player == null)
			{
				CanvasGroup.alpha = 0f;
				return;
			}

			if (_bestHunter != GameManager.BestHunter)
			{
				_bestHunter = GameManager.BestHunter;

				var hunterObject = GameManager.Runner.GetPlayerObject(_bestHunter);
				BestHunter.text = hunterObject != null ? hunterObject.GetComponent<Player>().Nickname : string.Empty;
				BestHunter.gameObject.SetActive(hunterObject != null);
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

			if (HungerBar != null && player.MaxHunger > 0f)
			{
				HungerBar.fillAmount = Mathf.Clamp01(player.Hunger / player.MaxHunger);
			}

			if (_lastChickens != player.ChickenKills)
			{
				if (player.ChickenKills > _lastChickens && player.ChickenKills > 0)
				{
					AudioSource.PlayOneShot(ChickenKillClip);
				}

				_lastChickens = player.ChickenKills;

				CanvasGroup.alpha = 1f;
				ChickenCount.text = $"\u00d7{_lastChickens}";
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
