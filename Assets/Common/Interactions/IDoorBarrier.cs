namespace Starter.Common.Interactions
{
	/// <summary>
	/// Shared contract for any door-like barrier that can block an actor's path and be forced open by the
	/// state authority (used by AI bots — see <c>BotBrain.TryOpenBlockingDoor</c>). Lets path-clearing code
	/// treat every kind of door uniformly without knowing the concrete type:
	///  • <c>Door</c> — a freely toggleable door; force-open = open it.
	///  • <c>NpcExit</c> — a building lockdown door; force-open = breach it (it can't be re-closed this round).
	///
	/// This is deliberately a thin interface, not a class merge: the two doors keep their distinct
	/// player-facing interaction behavior. This only unifies "is it passable, and how does an authority
	/// shove through it" — the single concern shared by both.
	/// </summary>
	public interface IDoorBarrier
	{
		/// <summary>True when the barrier currently lets actors through (open or breached).</summary>
		bool IsPassable { get; }

		/// <summary>State authority only: force the barrier into a passable state for AI/bots. No-op off the
		/// state authority or when already passable. Door → opens; NpcExit → breaches.</summary>
		void AuthorityForceOpen();
	}
}
