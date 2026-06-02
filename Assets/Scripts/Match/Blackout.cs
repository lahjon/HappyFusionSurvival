namespace Starter.Shooter
{
	/// <summary>
	/// "Blackout" — kills all electricity in the town for its duration, then restores it. Leans on the existing
	/// networked <see cref="LightGrid"/>: the event flips every zone dark via <see cref="LightGrid.HostSetBlackout"/>
	/// (host-only), and the grid's replicated <c>ZonePower</c> + <c>PowerChanged</c> drive every powered device / light
	/// on every peer. When the event ends the grid resumes its normal phase-driven schedule.
	///
	/// Forced-only for now (debug / scripted beat) — give it an auto-trigger heuristic in <see cref="CanTrigger"/> if
	/// you want the director to fire it on its own.
	/// </summary>
	public sealed class Blackout : MatchEvent
	{
		public override MatchEventType Type => MatchEventType.Blackout;

		public override float RandomBeatWeight => 1f;

		public override bool CanTrigger(int tick)
		{
			if (ForcePending == false) return false;
			ClearForce();
			return true;
		}

		protected override void OnStart(int tick)
		{
			LightGrid.Instance?.HostSetBlackout(true);
			Host.PublishEvent(Type, Host.BlackoutDurationSeconds);
		}

		protected override void OnTick(int tick)
		{
			if (Host.EventTimer.Expired(Runner))
				Resolve();
		}

		protected override void OnEnd()
		{
			// Release the override; the grid resumes its phase-driven (edges-first) schedule next tick.
			LightGrid.Instance?.HostSetBlackout(false);
		}
	}
}
