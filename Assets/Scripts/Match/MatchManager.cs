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

		/// <summary>Debug override (the <c>arm</c> console command): while true, PvP damage and weapon fire are
		/// allowed regardless of <see cref="Phase"/> — letting you playtest combat during the Day/Lobby. Stays on
		/// until explicitly disarmed (no timer). Networked so every peer agrees with the host's gate in
		/// <c>Health.PvpDamageBlocked</c> / <c>Player.CanFireWeaponNow</c>.</summary>
		[Networked] public NetworkBool DebugArmForced { get; private set; }

		/// <summary>True while the <c>arm</c> debug override is active (PvP forced on regardless of phase).</summary>
		public bool IsPvpForced => DebugArmForced;

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

		/// <summary>Set by the debug console (set_daytime / set_nighttime). While true the host stops auto-advancing
		/// the phase machine — the forced phase holds indefinitely instead of expiring or resolving a winner.
		/// Authority-only; the phase machine only runs on the authority so it need not be networked.</summary>
		private bool _debugHoldPhase;

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false) return;
			if (_debugHoldPhase) return;

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

			// Map each team to a distinct staging zone (used by the Night PvP-arming gate). Must run after
			// teams are assigned so it can see the team ids.
			ZoneManager.Instance?.AssignZones();

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
		// Debug (Quantum Console)
		// =========================================================================

		/// <summary>Debug-only: force the round into <paramref name="phase"/> from any peer. Routes to the state
		/// authority, which enters the phase and freezes the machine so it won't auto-advance or resolve a winner
		/// (set_nighttime would otherwise hit <see cref="CheckNightWin"/> and end instantly with no teams assigned).
		/// Use <see cref="ReturnToLobby"/> / <see cref="BeginMatch"/> to resume normal flow.</summary>
		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_DebugForcePhase(MatchPhase phase)
		{
			if (HasStateAuthority == false) return;
			_debugHoldPhase = true;
			WinningTeamId = -1;
			EnterPhase(phase);
		}

		/// <summary>Debug-only: force-enable PvP regardless of phase (override the no-combat-during-Day gate) so you
		/// can playtest combat in the Day/Lobby. Stays on until disarmed (<paramref name="on"/> = false) — no timer.
		/// Routes to the state authority so the networked <see cref="DebugArmForced"/> replicates to every peer's
		/// damage / fire gate.</summary>
		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_DebugArm(bool on)
		{
			if (HasStateAuthority == false) return;
			DebugArmForced = on;
		}

		/// <summary>Debug-only: end the round NOW in favour of <paramref name="winner"/>'s team — that team wins,
		/// every other team loses. Kills every living player not on the winning team first (so the eliminations are
		/// real, not just a declared result), then runs the normal win path. Only valid during <see cref="MatchPhase.Night"/>
		/// (no-op otherwise, or if the player has no team). Routes to the state authority and clears any
		/// <c>set_nighttime</c> hold so the MatchOver → Lobby flow resumes.</summary>
		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_DebugTriggerVictory(PlayerRef winner)
		{
			if (HasStateAuthority == false) return;
			if (Phase != MatchPhase.Night) return;

			int teamId = TeamManager != null ? TeamManager.TeamOf(winner) : -1;
			if (teamId < 0) return;

			// Wipe every living enemy (anyone not on the winning team) outright. AuthorityKill bypasses the
			// downed hook so they die rather than bleed out, making the win condition resolve cleanly.
			var gm = FindAnyObjectByType<GameManager>();
			if (gm != null)
			{
				var players = gm.Players;
				for (int i = 0; i < players.Count; i++)
				{
					var p = players[i];
					if (p == null || p.Object == null || p.Health == null) continue;
					if (p.Health.IsAlive == false) continue;
					if (TeamManager != null && TeamManager.TeamOf(p.Owner) == teamId) continue;
					p.Health.AuthorityKill();
				}
			}

			_debugHoldPhase = false; // resume the normal MatchOver → Lobby flow after the forced win.
			EndNightForWinner(teamId);
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
