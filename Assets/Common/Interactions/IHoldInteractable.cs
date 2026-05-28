namespace Starter.Common.Interactions
{
	/// <summary>
	/// Mixin capability for an <see cref="IInteractable"/> whose action fires only when
	/// the player holds Interact for <see cref="HoldSeconds"/>. The scanner notifies the
	/// target on start, on cancel (released early / looked away / no longer interactable),
	/// and on completion, so the target can drive a paused-while-held mechanic (revive a
	/// downed player, lockpick a door, etc.) instead of just receiving the final fire.
	///
	/// Parallel to <see cref="IPickupableStation"/>. If both are present on the same
	/// target, pickup wins — the hold path is single-purpose per object.
	/// </summary>
	public interface IHoldInteractable
	{
		/// <summary>Seconds the player must keep Interact held before the action fires.</summary>
		float HoldSeconds { get; }

		/// <summary>Called locally when the hold begins. Implementer should send its own RPC
		/// (e.g. "I'm reviving") so the target can pause/track per-reviver state.</summary>
		void LocalRequestHoldStart();

		/// <summary>Called locally if the hold is aborted before completion (release, looked
		/// away, target became uninteractable). Implementer should send the matching "cancel"
		/// RPC so the target can drop reviver-count / unpause its bleed-out timer.</summary>
		void LocalRequestHoldCancel();

		/// <summary>Called locally when the hold timer fully elapses. Implementer should
		/// send the "complete" RPC; the target re-validates and applies the effect.</summary>
		void LocalRequestHoldComplete();
	}
}
