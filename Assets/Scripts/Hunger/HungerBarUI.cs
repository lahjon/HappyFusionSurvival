using Starter.Shooter;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.Hunger
{
	/// <summary>
	/// Self-contained hunger bar HUD, lifted out of <c>UIShooter</c>. Drop on a UI object, assign <see cref="Bar"/>, and
	/// it mirrors the local player's <see cref="HungerSystem"/> each frame. When hunger is disabled (no HungerSystem on
	/// the local player) the bar hides itself, so leaving this unwired or inactive costs nothing.
	///
	/// Local view only — reads the replicated networked fullness, writes nothing. Part of the dormant Starter.Hunger
	/// module. Resolves the local player through <c>GameManager.LocalPlayer</c> (lazily found), so it needs no wiring
	/// beyond the bar image.
	/// </summary>
	public sealed class HungerBarUI : MonoBehaviour
	{
		[Tooltip("Filled image whose fillAmount tracks Hunger / MaxHunger.")]
		public Image Bar;

		[Tooltip("How fast the bar eases toward the current value.")]
		public float LerpSpeed = 6f;

		private GameManager _gameManager;
		private float _displayed = 1f;

		private void Update()
		{
			if (_gameManager == null)
				_gameManager = FindAnyObjectByType<GameManager>();

			var player = _gameManager != null ? _gameManager.LocalPlayer : null;
			var hunger = player != null ? player.GetComponent<HungerSystem>() : null;

			bool active = Bar != null && hunger != null && hunger.MaxHunger > 0f;
			if (Bar != null && Bar.gameObject.activeSelf != active)
				Bar.gameObject.SetActive(active);
			if (active == false)
				return;

			float target = Mathf.Clamp01(hunger.Hunger / hunger.MaxHunger);
			_displayed = Mathf.Lerp(_displayed, target, Time.deltaTime * LerpSpeed);
			Bar.fillAmount = _displayed;
		}
	}
}
