namespace Starter.Shooter
{
	/// <summary>
	/// Feedback sink implemented by a held item's visual rig (HeldWeapon, FistPunchAnimator, future
	/// creature claws). The owner reads the active <see cref="CombatAction"/> from the item's
	/// <see cref="WeaponCapability"/> (data on the asset) and calls these hooks on the spawned hand
	/// prefab to play audio / swing / recoil / charge-pose when the authoritative fire counter
	/// advances. Separates "what the item does" (capability data) from "how the held model reacts"
	/// (this visual rig).
	/// </summary>
	public interface IHeldVisual
	{
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
