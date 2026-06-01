using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Per-actor networked runtime for CombatAction. Holds the shared cooldown and
	/// charge tick so the same primitive serves players, AI, and anything else
	/// that fires actions. Mutations are state-authority-only and replicate via
	/// the [Networked] fields.
	/// </summary>
	public sealed class ActionInvoker : NetworkBehaviour
	{
		[Networked]
		public TickTimer Cooldown { get; set; }
		// Independent cooldown for the secondary (RMB) action so a throw doesn't share the
		// primary-fire timer. Only used by TryFire(secondary: true).
		[Networked]
		public TickTimer SecondaryCooldown { get; set; }
		// 0 = not charging. Tick number when the current charge started.
		[Networked]
		public int ChargeStartTick { get; set; }
		[Networked]
		public NetworkBool LastFireWasCharged { get; set; }

		public bool CanFire => Cooldown.ExpiredOrNotRunning(Runner);
		public bool CanFireSecondary => SecondaryCooldown.ExpiredOrNotRunning(Runner);
		public bool IsCharging => ChargeStartTick > 0;

		public float ChargeSeconds
		{
			get
			{
				if (ChargeStartTick <= 0 || Runner == null) return 0f;
				int elapsed = (int)Runner.Tick - ChargeStartTick;
				return elapsed > 0 ? elapsed * Runner.DeltaTime : 0f;
			}
		}

		public float ChargeProgress(CombatAction action)
		{
			if (action == null || action.Charge.Enabled == false) return 0f;
			return Mathf.Clamp01(ChargeSeconds / Mathf.Max(0.01f, action.Charge.ThresholdSeconds));
		}

		// Note: the charge/fire methods deliberately do NOT gate on HasStateAuthority.
		// They are called from Player.ProcessInput (after GetInput succeeded), which runs
		// on both the state authority and the input authority — input auth is expected to
		// predict [Networked] writes (cooldown, charge tick, fire count). Mirrors the
		// original Player.Fire / ProcessFireInput predictive pattern. Anything that must
		// only mutate authoritatively (e.g. ApplyKnockback) gates itself internally.

		public void StartCharge()
		{
			ChargeStartTick = Mathf.Max(1, (int)Runner.Tick);
		}

		public void CancelCharge()
		{
			ChargeStartTick = 0;
		}

		/// <summary>
		/// Stops the charge and reports how long it was held. Returns false when no charge was active.
		/// </summary>
		public bool ReleaseCharge(out float chargeSeconds)
		{
			chargeSeconds = 0f;
			if (ChargeStartTick <= 0) return false;

			int elapsed = (int)Runner.Tick - ChargeStartTick;
			chargeSeconds = Mathf.Max(0f, elapsed * Runner.DeltaTime);
			ChargeStartTick = 0;
			return true;
		}

		/// <summary>
		/// Resolves the action, sets the cooldown, and records whether the release was
		/// charged. Runs on every predicting peer (input authority + state authority);
		/// state authority's result is what gets reconciled to proxies.
		/// </summary>
		public ActionHit TryFire(CombatAction action, in ActorContext ctx, bool charged, bool secondary = false)
		{
			var result = default(ActionHit);
			if (action == null) return result;
			if ((secondary ? CanFireSecondary : CanFire) == false) return result;
			// Pre-fire gate (e.g. ammo check on ProjectileAction). Runs on every predicting peer
			// — IA reads networked Slots, so the check resolves identically on SA and IA without
			// an extra RPC and lets a dry-fire abort cleanly (no cooldown, no recoil, no audio).
			if (action.CanExecute(in ctx) == false) return result;

			result = action.Execute(in ctx, charged);
			result.DidFire = true;

			if (action.Cooldown > 0f)
			{
				var timer = TickTimer.CreateFromSeconds(Runner, action.Cooldown);
				if (secondary) SecondaryCooldown = timer;
				else Cooldown = timer;
			}

			LastFireWasCharged = charged;
			return result;
		}
	}
}
