namespace Starter.Shooter
{
	/// <summary>
	/// Identifies which <see cref="MatchEvent"/> is currently active on the <see cref="GameHostManager"/> director.
	/// This is the networked discriminator clients read to know what (if anything) is happening — the host-side event
	/// objects own their own logic, but only this enum + the generic event slot (target team + timer) cross the wire.
	/// Stored as a byte so it is cheap to network and OnChangedRender fires on every peer.
	/// </summary>
	public enum MatchEventType : byte
	{
		None      = 0,
		Hunt      = 1,
		Blackout  = 2,
	}
}
