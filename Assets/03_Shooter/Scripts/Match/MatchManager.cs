using System;
using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Single source of truth for the round phase (<see cref="MatchPhase"/>). State authority advances the
	/// phase machine in <see cref="FixedUpdateNetwork"/>; every other peer reacts via <see cref="OnPhaseChangedRender"/>
	/// (vendor visibility, lighting, audio, win-condition gates). Damage/vendor/quest logic must read <see cref="Phase"/>
	/// from here, never from local <c>Time.time</c>.
	///
	/// Scene-placed singleton — drop a NetworkObject in 03_Shooter.unity with this + <see cref="TeamManager"/> on it.
	/// </summary>
	public sealed class MatchManager : NetworkBehaviour
	{
		public static MatchManager Instance { get; private set; }

		/// <summary>Fires on every peer when <see cref="Phase"/> changes. Subscribers must be idempotent — this can
		/// fire on late-joiners replaying the current phase.</summary>
		public static event Action<MatchPhase> PhaseChanged;

		[Header("Phase durations (seconds)")]
		[Tooltip("Town / prep window. Vendors open, PvP off. Default: 15 minutes.")]
		[Min(10f)] public float DayDuration = 15f * 60f;

		[Tooltip("Brief transition between Day and Night. Lights flicker, vendors close, music distorts. Default: 30s.")]
		[Min(1f)] public float DuskWarningDuration = 30f;

		[Tooltip("Maximum PvP duration. Round can end earlier when only one team has living members. Default: 15 minutes.")]
		[Min(10f)] public float NightMaxDuration = 15f * 60f;

		[Tooltip("Scoreboard display after match resolves before returning to Lobby. Default: 20s.")]
		[Min(1f)] public float MatchOverDuration = 20f;

		[Header("References (auto-found on Spawned if null)")]
		public TeamManager TeamManager;

		// =========================================================================
		// Networked state
		// =========================================================================

		/// <summary>Current phase. Only the state authority writes; all peers render via OnChangedRender.</summary>
		[Networked, OnChangedRender(nameof(OnPhaseChangedRender))]
		public MatchPhase Phase { get; private set; }

		/// <summary>Counts down the active phase. Lobby uses default (not running) — host advances on demand.</summary>
		[Networked] public TickTimer PhaseTimer { get; private set; }

		/// <summary>Incremented on every Lobby → Day transition. Used for telemetry / rematch labelling.</summary>
		[Networked] public int RoundIndex { get; private set; }

		/// <summary>Set when a winner is found. -1 = no winner yet (or draw). Read by UI on MatchOver.</summary>
		[Networked] public int WinningTeamId { get; private set; } = -1;

		// =========================================================================
		// Lifecycle
		// =========================================================================

		public override void Spawned()
		{
			Instance = this;

			if (TeamManager == null)
				TeamManager = GetComponent<TeamManager>();
			if (TeamManager == null)
				TeamManager = FindAnyObjectByType<TeamManager>();

			// Late-joiners replay the current phase locally so subscribers get a chance to set up.
			PhaseChanged?.Invoke(Phase);

			if (HasStateAuthority && Phase == MatchPhase.Lobby)
			{
				// Fresh boot: ensure clean Lobby state.
				WinningTeamId = -1;
			}
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (Instance == this) Instance = null;
		}

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false) return;

			switch (Phase)
			{
				case MatchPhase.Lobby:
					// No timer — host advances explicitly via BeginMatch().
					break;

				case MatchPhase.Day:
				case MatchPhase.DuskWarning:
					if (PhaseTimer.Expired(Runner))
						AdvanceNextPhase();
					break;

				case MatchPhase.Night:
					CheckNightWin();
					if (Phase == MatchPhase.Night && PhaseTimer.Expired(Runner))
					{
						// Timer expired — apply tiebreaker.
						WinningTeamId = TeamManager != null ? TeamManager.TiebreakWinningTeam() : -1;
						EnterPhase(MatchPhase.MatchOver);
					}
					break;

				case MatchPhase.MatchOver:
					if (PhaseTimer.Expired(Runner))
						EnterPhase(MatchPhase.Lobby);
					break;
			}
		}

		// =========================================================================
		// Host-driven transitions
		// =========================================================================

		/// <summary>Host-only entry point — starts a new round from Lobby. No-op if already running or not authority.</summary>
		public void BeginMatch()
		{
			if (HasStateAuthority == false) return;
			if (Phase != MatchPhase.Lobby) return;

			RoundIndex++;
			WinningTeamId = -1;

			if (TeamManager != null)
				TeamManager.AssignTeams();

			EnterPhase(MatchPhase.Day);
		}

		/// <summary>Force-resets to Lobby (debug / admin). State-authority only.</summary>
		public void ReturnToLobby()
		{
			if (HasStateAuthority == false) return;
			if (TeamManager != null)
				TeamManager.ClearAssignments();
			WinningTeamId = -1;
			EnterPhase(MatchPhase.Lobby);
		}

		/// <summary>Called from <see cref="TeamManager"/> (or anywhere a kill resolves) when only one team should remain.</summary>
		public void EndNightForWinner(int winningTeamId)
		{
			if (HasStateAuthority == false) return;
			if (Phase != MatchPhase.Night) return;
			WinningTeamId = winningTeamId;
			EnterPhase(MatchPhase.MatchOver);
		}

		// =========================================================================
		// Internal phase machine
		// =========================================================================

		private void AdvanceNextPhase()
		{
			switch (Phase)
			{
				case MatchPhase.Day:         EnterPhase(MatchPhase.DuskWarning); break;
				case MatchPhase.DuskWarning: EnterPhase(MatchPhase.Night);       break;
				case MatchPhase.Night:       EnterPhase(MatchPhase.MatchOver);   break;
				case MatchPhase.MatchOver:   EnterPhase(MatchPhase.Lobby);       break;
			}
		}

		private void EnterPhase(MatchPhase next)
		{
			Phase = next;
			PhaseTimer = next switch
			{
				MatchPhase.Day         => TickTimer.CreateFromSeconds(Runner, DayDuration),
				MatchPhase.DuskWarning => TickTimer.CreateFromSeconds(Runner, DuskWarningDuration),
				MatchPhase.Night       => TickTimer.CreateFromSeconds(Runner, NightMaxDuration),
				MatchPhase.MatchOver   => TickTimer.CreateFromSeconds(Runner, MatchOverDuration),
				_                      => default, // Lobby has no timer
			};

			if (next == MatchPhase.Lobby && TeamManager != null)
				TeamManager.ClearAssignments();
		}

		private void CheckNightWin()
		{
			if (TeamManager == null) return;
			int livingTeams = TeamManager.CountLivingTeams(out int lastTeamId);
			if (livingTeams <= 1)
			{
				// 1 team alive → that team wins. 0 teams alive (rare, simultaneous wipe) → no winner.
				WinningTeamId = livingTeams == 1 ? lastTeamId : -1;
				EnterPhase(MatchPhase.MatchOver);
			}
		}

		private void OnPhaseChangedRender()
		{
			PhaseChanged?.Invoke(Phase);
		}

		// =========================================================================
		// Read helpers (UI, aesthetics)
		// =========================================================================

		/// <summary>Seconds remaining in the current phase, or 0 if no timer (Lobby).</summary>
		public float RemainingPhaseSeconds => PhaseTimer.RemainingTime(Runner).GetValueOrDefault();

		public bool IsPvpActive => Phase == MatchPhase.Night;
	}
}
