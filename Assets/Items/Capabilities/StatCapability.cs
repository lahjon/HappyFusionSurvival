using Fusion;
using Starter.Common.Inventory;
using Starter.Hunger;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Consumable facet that restores a player stat (health, stamina, ...) on use. One capability
	/// covering all "restore N of stat X" effects — pick the stat and amount on the item asset rather
	/// than adding a separate capability type per stat. Mirrors the old HealingCapability/StaminaCapability,
	/// consolidated per the "reuse before adding" principle: the two were near-identical lookup+grant shells.
	/// </summary>
	[System.Serializable]
	public sealed class StatCapability : ConsumableCapability
	{
		public enum Stat { Health, Stamina, Hunger }

		[Tooltip("Which stat this restores.")]
		public Stat Type = Stat.Health;

		[Tooltip("Amount restored when consumed. Health is capped at max HP; stamina at MaxStamina.")]
		[Min(0f)] public float Amount = 1f;

		public override void Apply(GameObject target, NetworkRunner runner)
		{
			if (target == null) return;

			switch (Type)
			{
				case Stat.Health:
					if (target.TryGetComponent<Health>(out var health))
						health.Heal(Mathf.RoundToInt(Amount));
					break;

				case Stat.Stamina:
					if (target.TryGetComponent<Player>(out var player))
						player.RestoreStamina(Amount);
					break;

				case Stat.Hunger:
					if (target.TryGetComponent<HungerSystem>(out var hunger))
						hunger.AddHunger(Amount);
					break;
			}
		}
	}
}
