namespace Starter.Shooter
{
	/// <summary>
	/// Phase of a single round in the Purge × Stardew loop. Driven by <see cref="MatchManager"/> on state authority.
	/// Stored as a byte so it is cheap to network and OnChangedRender fires on every peer.
	/// </summary>
	public enum MatchPhase : byte
	{
		Lobby       = 0,
		Day         = 1,
		DuskWarning = 2,
		Night       = 3,
		MatchOver   = 4,
	}
}
