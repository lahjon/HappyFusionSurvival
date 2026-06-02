using Fusion;

namespace Starter.Shooter
{
	/// <summary>
	/// Base class for a dynamic Night-phase event driven by the <see cref="GameHostManager"/> director (the "Purge
	/// director"). Concrete events — <see cref="TheHunt"/> and future ones — subclass this to add their own trigger
	/// condition and per-tick behaviour.
	///
	/// <para><b>Host-only, plain C# (not a NetworkBehaviour).</b> All lifecycle hooks run on the state authority only.
	/// Events cannot hold <c>[Networked]</c> state themselves (Fusion only weaves it onto NetworkBehaviours), so any
	/// state clients must see is published through the director's fixed generic slot via
	/// <see cref="GameHostManager.PublishEvent"/> (<see cref="MatchEventType"/> + target team + a <see cref="TickTimer"/>).
	/// Bookkeeping the host needs but clients don't (snapshots, scoring) stays in plain fields here.</para>
	///
	/// <para>Lifecycle: the director polls idle events with <see cref="CanTrigger"/>; when one returns true it is
	/// <see cref="Begin"/>-run, ticked every <c>FixedUpdateNetwork</c> via <see cref="Tick"/>, and ends when the event
	/// calls <see cref="Resolve"/> (or the director cancels it on leaving Night).</para>
	/// </summary>
	public abstract class MatchEvent
	{
		/// <summary>The director that owns this event. Use it to reach shared services (roster/passivity queries,
		/// <see cref="GameHostManager.PublishEvent"/>, <see cref="GameHostManager.PurgeTeam"/>) and the runner.</summary>
		protected GameHostManager Host { get; private set; }

		protected NetworkRunner Runner => Host.Runner;

		/// <summary>Networked discriminator this event publishes while active.</summary>
		public abstract MatchEventType Type { get; }

		/// <summary>Relative likelihood the director picks this event for a random "beat". 0 (default) = never chosen by
		/// random beats — the event only fires via its own <see cref="CanTrigger"/> heuristic or a manual force. Higher
		/// = more likely relative to the other eligible events.</summary>
		public virtual float RandomBeatWeight => 0f;

		/// <summary>True between <see cref="Begin"/> and <see cref="Resolve"/>.</summary>
		public bool IsActive { get; private set; }

		/// <summary>Set when the event has been manually forced (debug / scripted beat) and is waiting for the director's
		/// next <see cref="CanTrigger"/> poll. Subclasses check this to start regardless of their auto-trigger condition,
		/// and clear it with <see cref="ClearForce"/> once consumed.</summary>
		protected bool ForcePending { get; private set; }

		internal void Bind(GameHostManager host) => Host = host;

		/// <summary>Request that this event start at the next opportunity (bypassing its auto-trigger heuristic). Subclasses
		/// that need a parameter (target team / zone) override with their own typed overload that calls this.</summary>
		public virtual void RequestForce() => ForcePending = true;

		/// <summary>Consume the pending force flag (call once a forced trigger has been accepted/rejected).</summary>
		protected void ClearForce() => ForcePending = false;

		/// <summary>Host-only. The director asks every idle event whether it wants to start this tick. Return true to be
		/// started immediately. Implementations should also stash whatever they computed (e.g. the chosen team) so
		/// <see cref="OnStart"/> can use it without recomputing.</summary>
		public abstract bool CanTrigger(int tick);

		// ---- Director-driven lifecycle (do not call from gameplay code) ----

		internal void Begin(int tick)
		{
			if (IsActive) return;
			IsActive = true;
			OnStart(tick);
		}

		internal void Tick(int tick)
		{
			if (IsActive) OnTick(tick);
		}

		/// <summary>Host-only. Ends the event cleanly (success, failure-after-penalty, or director cancel on Night exit).
		/// Clears the director's networked slot and starts the inter-event cooldown.</summary>
		protected internal void Resolve()
		{
			if (IsActive == false) return;
			IsActive = false;
			OnEnd();
			Host.NotifyEventResolved(this);
		}

		// ---- Subclass hooks (host-only) ----

		/// <summary>Set up the event and publish its client-visible state via <see cref="GameHostManager.PublishEvent"/>.</summary>
		protected abstract void OnStart(int tick);

		/// <summary>Per-tick logic while active. Call <see cref="Resolve"/> when the event should end.</summary>
		protected abstract void OnTick(int tick);

		/// <summary>Optional cleanup when the event ends (host-only). Networked slot is cleared by the director.</summary>
		protected virtual void OnEnd() { }
	}
}
