using UnityEngine;
using UnityEngine.UI;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only fullscreen black overlay that drives its alpha from <see cref="GameManager.CurrentSleepPhase"/>
	/// + <see cref="GameManager.SleepPhaseTimer"/>. Designed to sit on the existing HUD canvas as a stretched
	/// black Image — assign the Image in the inspector and the script handles the rest.
	///
	/// Alpha curve (driven by networked phase, so every peer fades together):
	///   None       -> 0
	///   FadingIn   -> 0 .. 1 over <c>SleepFadeInDuration</c>
	///   FadingOut  -> 1 .. 0 over <c>SleepFadeOutDuration</c>
	/// </summary>
	public sealed class SleepFadeOverlay : MonoBehaviour
	{
		[Tooltip("The fullscreen black Image whose alpha is driven. Should be the topmost UI element on the HUD canvas with raycastTarget = false so it doesn't block input.")]
		public Image Overlay;

		[Tooltip("Reference to the scene's GameManager. If null, the script searches for one on Awake.")]
		public GameManager GameManager;

		private void Awake()
		{
			if (GameManager == null) GameManager = FindAnyObjectByType<GameManager>();
			SetAlpha(0f);
		}

		private void Update()
		{
			if (Overlay == null) return;

			var gm = GameManager;
			if (gm == null || gm.Object == null || gm.Object.IsValid == false || gm.Runner == null)
			{
				SetAlpha(0f);
				return;
			}

			float duration = gm.CurrentSleepPhaseDuration;
			float remaining = gm.SleepPhaseTimer.RemainingTime(gm.Runner) ?? 0f;
			float progress = duration > 0f ? 1f - Mathf.Clamp01(remaining / duration) : 1f;

			float alpha;
			switch (gm.CurrentSleepPhase)
			{
				case SleepPhase.FadingIn:  alpha = progress;        break;
				case SleepPhase.FadingOut: alpha = 1f - progress;   break;
				default:                   alpha = 0f;              break;
			}

			SetAlpha(alpha);
		}

		private void SetAlpha(float a)
		{
			if (Overlay == null) return;
			var c = Overlay.color;
			if (Mathf.Approximately(c.a, a)) return;
			c.a = a;
			Overlay.color = c;
		}
	}
}
