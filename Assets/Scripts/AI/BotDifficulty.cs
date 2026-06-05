namespace Starter.Shooter
{
	/// <summary>
	/// AI skill tier for a bot-controlled <see cref="Player"/>, chosen at spawn (see <see cref="GameManager.AddBots"/>)
	/// and applied by <see cref="BotBrain"/>. It governs two knobs: how often the bot pulls the trigger (a fraction of
	/// its weapon's natural fire rate) and how accurately it aims (the share of shots aimed dead-on; the rest are thrown
	/// deliberately wide). <see cref="Retarded"/> ignores the target entirely and sprays in random directions.
	///
	/// <para>byte-backed so it serialises cleanly through Fusion RPC params (<c>GameManager.RPC_DebugAddBots</c>) and the
	/// git-ignored <see cref="HappyTestConfig"/> JSON.</para>
	/// </summary>
	public enum BotDifficulty : byte
	{
		Retarded = 0,
		Easy     = 1,
		Medium   = 2,
		Hard     = 3,
	}

	public static class BotDifficultyExtensions
	{
		/// <summary>Resolves the tuning for a difficulty tier.
		/// <paramref name="fireRateFactor"/> scales the weapon's natural cadence (1 = a shot every cooldown, 0.25 = every
		/// fourth). <paramref name="hitRate"/> is the probability a given shot is aimed dead-on (the rest are aimed to
		/// miss). <paramref name="aimsAtTarget"/> is false only for <see cref="BotDifficulty.Retarded"/>, which fires in
		/// random directions without tracking the enemy at all.</summary>
		public static void GetProfile(this BotDifficulty difficulty, out float fireRateFactor, out float hitRate, out bool aimsAtTarget)
		{
			switch (difficulty)
			{
				case BotDifficulty.Easy:   fireRateFactor = 0.25f; hitRate = 0.20f; aimsAtTarget = true;  break;
				case BotDifficulty.Medium: fireRateFactor = 0.50f; hitRate = 0.30f; aimsAtTarget = true;  break;
				case BotDifficulty.Hard:   fireRateFactor = 1.00f; hitRate = 0.40f; aimsAtTarget = true;  break;
				case BotDifficulty.Retarded:
				default:                   fireRateFactor = 0.50f; hitRate = 0.00f; aimsAtTarget = false; break;
			}
		}
	}
}
