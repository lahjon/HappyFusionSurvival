using System;

namespace Starter.Shooter
{
	/// <summary>
	/// "The Hunt" — the first <see cref="MatchEvent"/>. When a team has gone passive for too long the director forces
	/// them to become the Hunter: they must eliminate any rival team within the hunt duration or the whole hunter team
	/// is purged. Can also be forced manually (debug / scripted beat) via <see cref="RequestForce"/>.
	///
	/// All state here is host-side. The client-visible part (which team is the hunter + the countdown) is published
	/// through <see cref="GameHostManager.PublishEvent"/> as <see cref="MatchEventType.Hunt"/> + target team + timer.
	/// </summary>
	public sealed class TheHunt : MatchEvent
	{
		public override MatchEventType Type => MatchEventType.Hunt;

		// Also fires reactively on passivity, so a lower random weight keeps it from dominating the beats.
		public override float RandomBeatWeight => 0.5f;

		// The team currently designated hunter (decided in CanTrigger, used from OnStart onward).
		private int _hunterTeam = -1;

		// Rival teams that were alive when the hunt started — wiping any of them is a successful hunt.
		private readonly bool[] _targetTeams = new bool[TeamManager.MaxPlayers];

		// Pending manual force target: -1 = auto-pick the most passive team.
		private int _forcedTeam = -1;

		/// <summary>Queue a manual force (debug / scripted). Picked up by the next <see cref="CanTrigger"/>.
		/// <paramref name="teamId"/> &lt; 0 auto-picks the most passive living team.</summary>
		public void RequestForce(int teamId)
		{
			_forcedTeam = teamId;
			RequestForce();
		}

		public override bool CanTrigger(int tick)
		{
			if (ForcePending)
			{
				ClearForce();
				int forced = _forcedTeam >= 0 ? _forcedTeam : Host.MostPassiveLivingTeam(tick, out _, out _);
				_forcedTeam = -1;
				if (forced < 0 || Host.TeamIsLiving(forced) == false || Host.CountLivingTeams() < 2)
					return false;
				_hunterTeam = forced;
				return true;
			}

			int passiveThresholdTicks = Host.SecondsToTicks(Host.PassivitySeconds);
			int candidate = Host.MostPassiveLivingTeam(tick, out int idleTicks, out int livingTeams);

			if (livingTeams < 2 || candidate < 0 || idleTicks < passiveThresholdTicks)
				return false;

			_hunterTeam = candidate;
			return true;
		}

		protected override void OnStart(int tick)
		{
			// Snapshot the rival teams alive right now — eliminating any of them counts as success.
			Array.Clear(_targetTeams, 0, _targetTeams.Length);
			foreach (var p in Host.LivingPlayers())
			{
				int t = Host.TeamOf(p);
				if (t >= 0 && t < _targetTeams.Length && t != _hunterTeam)
					_targetTeams[t] = true;
			}

			Host.PublishEvent(Type, Host.HuntDurationSeconds, targetTeam: _hunterTeam);
		}

		protected override void OnTick(int tick)
		{
			// Hunter wiped out by normal combat mid-hunt → just end (no extra penalty, they're already gone).
			if (Host.TeamIsLiving(_hunterTeam) == false)
			{
				Resolve();
				return;
			}

			// Success: any team that was alive at hunt-start is now extinct.
			for (int t = 0; t < _targetTeams.Length; t++)
			{
				if (_targetTeams[t] && Host.TeamIsLiving(t) == false)
				{
					Resolve();
					return;
				}
			}

			// Failure: ran out of time without eliminating anyone → purge the hunter team.
			if (Host.EventTimer.Expired(Runner))
			{
				Host.PurgeTeam(_hunterTeam);
				Resolve();
			}
		}

		protected override void OnEnd()
		{
			_hunterTeam = -1;
		}
	}
}
