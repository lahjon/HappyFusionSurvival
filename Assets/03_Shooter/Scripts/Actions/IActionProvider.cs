using System.Collections.Generic;

namespace Starter.Shooter
{
	/// <summary>
	/// Implemented by anything a Player or AI can "hold" or attach actions to —
	/// HeldWeapon, FistPunchAnimator, future creature claws, etc. The owner reads
	/// Actions to pick what to fire and calls the feedback hooks when the
	/// authoritative fire counter advances.
	/// </summary>
	public interface IActionProvider
	{
		IReadOnlyList<CombatAction> Actions { get; }

		/// <summary>Play the action's audio clip on the holder's local AudioSource.</summary>
		void PlayAttackSound(CombatAction action);

		/// <summary>Trigger melee swing/punch animation. Called when the just-fired action's Style is Melee.</summary>
		void PlayMeleeFeedback(bool charged);

		/// <summary>Trigger muzzle particle + recoil kick. Called when the just-fired action's Style is Ranged.</summary>
		void PlayRangedFeedback();

		/// <summary>Drive the pulled-back charge pose on the visual. progress is 0..1 toward the charge threshold.</summary>
		void SetCharging(bool charging, float progress);
	}
}
