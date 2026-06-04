using System;
using System.Linq;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

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

		/// <summary>Fires on every peer when <see cref="IsPreNight"/> toggles: <c>true</c> = cut every light and sound
		/// dark/silent for the final beat before the Purge; <c>false</c> = restore (Night is starting, or DuskWarning was
		/// left). Subscribers (street lights, audio) must be idempotent — this can replay on late-joiners.</summary>
		public static event Action<bool> PreNightChanged;

		[Header("Phase durations (seconds)")]
		[Tooltip("Town / prep window. Vendors open, PvP off. Default: 15 minutes.")]
		[Min(10f)] public float DayDuration = 15f * 60f;

		[Tooltip("Transition window between Day and Night. The town siren sounds (once at the start, once 10s before " +
			"Night), vendors close, and NPCs retreat home. Default: 60s (1 min).")]
		[Min(1f)] public float DuskWarningDuration = 60f;

		[Tooltip("Final stretch of DuskWarning, this many seconds before Night, that fires the PreNight beat — every " +
			"light and sound cuts out, then snaps back when the Purge begins. Must be shorter than DuskWarningDuration. " +
			"Default: 5s.")]
		[Min(0.5f)] public float PreNightLeadSeconds = 5f;

		[Tooltip("Maximum PvP duration. Round can end earlier when only one team has living members. Default: 15 minutes.")]
		[Min(10f)] public float NightMaxDuration = 15f * 60f;

		[Tooltip("Scoreboard display after match resolves before returning to the lobby scene. Default: 20s.")]
		[Min(1f)] public float MatchOverDuration = 20f;

		[Header("Scene")]
		[Tooltip("Build index of the menu/lobby scene to return everyone to when the match is over. 00_MainMenu doubles as the lobby.")]
		public int LobbySceneIndex = 0;

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

		/// <summary>True only during the final <see cref="PreNightLeadSeconds"/> of <see cref="MatchPhase.DuskWarning"/> —
		/// the "everything goes dark and silent" beat right before the Purge. The state authority sets it as the dusk
		/// timer runs down and clears it on every phase transition (so Night, which immediately follows, restores the
		/// lights). Networked + OnChangedRender so every peer blacks out in lockstep, never off a local clock.</summary>
		[Networked, OnChangedRender(nameof(OnPreNightChangedRender))]
		public NetworkBool IsPreNight { get; private set; }

		/// <summary>Set when a winner is found. -1 = no winner yet (or draw). Read by UI on MatchOver.</summary>
		[Networked] public int WinningTeamId { get; private set; } = -1;

		/// <summary>Debug override (the <c>arm</c> console command): while true, PvP damage and weapon fire are
		/// allowed regardless of <see cref="Phase"/> — letting you playtest combat during the Day/Lobby. Stays on
		/// until explicitly disarmed (no timer). Networked so every peer agrees with the host's gate in
		/// <c>Health.PvpDamageBlocked</c> / <c>Player.CanFireWeaponNow</c>.</summary>
		[Networked] public NetworkBool DebugArmForced { get; private set; }

		/// <summary>True while the <c>arm</c> debug override is active (PvP forced on regardless of phase).</summary>
		public bool IsPvpForced => DebugArmForced;

		/// <summary>Debug override (the <c>time_scale</c> console command): networked game speed applied to every peer's
		/// local <see cref="Time.timeScale"/> via <see cref="OnTimeScaleChangedRender"/>. 1 = normal, 0 = paused, &gt;1 =
		/// faster. Networked so all peers (and late-joiners, on Spawned) agree on the simulation speed. Default 1.</summary>
		[Networked, OnChangedRender(nameof(OnTimeScaleChangedRender))]
		public float DebugTimeScale { get; private set; }

		/// <summary>True once the opening "wait for all players to load in" gate has passed and the round has begun
		/// (any phase past <see cref="MatchPhase.Lobby"/>). While false — the game scene is still in Lobby, waiting for
		/// everyone to spawn — freshly-spawned players are confined to their spawn area and cannot interact. Reads false
		/// before this MatchManager exists (game scene still loading), which keeps players locked during that window too.</summary>
		public static bool RoundStarted => Instance != null && Instance.Phase != MatchPhase.Lobby;

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

			// Late-joiners replay the current phase (and PreNight state) locally so subscribers get a chance to set up.
			PhaseChanged?.Invoke(Phase);
			PreNightChanged?.Invoke(IsPreNight);

			if (HasStateAuthority && Phase == MatchPhase.Lobby)
			{
				// Fresh boot: ensure clean Lobby state.
				WinningTeamId = -1;
			}

			// Networked default is 0 (which would pause everything) — the authority seeds a sane 1×. Every peer then
			// applies the replicated value locally here, since OnChangedRender may not fire for a late-joiner's
			// initial state sync.
			if (HasStateAuthority && DebugTimeScale <= 0f)
				DebugTimeScale = 1f;
			if (DebugTimeScale > 0f)
				Time.timeScale = DebugTimeScale;
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (Instance == this) Instance = null;

			// Don't let a debug slow-mo / pause leak into the menu scene after the match object despawns.
			Time.timeScale = 1f;
		}

		/// <summary>Set by the debug console (set_daytime / set_nighttime). While true the host stops auto-advancing
		/// the phase machine — the forced phase holds indefinitely instead of expiring or resolving a winner.
		/// Authority-only; the phase machine only runs on the authority so it need not be networked.</summary>
		private bool _debugHoldPhase;

		// Cached on the host for the auto-begin readiness scan.
		private GameManager _gameManager;
		// One-shot guard so the MatchOver→lobby scene load isn't re-issued every tick while the load is in flight.
		private bool _returningToLobbyScene;

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false) return;
			if (_debugHoldPhase) return;

			switch (Phase)
			{
				case MatchPhase.Lobby:
					// The game scene boots in Lobby with no UI; auto-begin the round once everyone has loaded in.
					TryAutoBegin();
					break;

				case MatchPhase.Day:
				case MatchPhase.DuskWarning:
					// Final beat of dusk: cut everything dark/silent just before the Purge. Night (next) clears it.
					if (Phase == MatchPhase.DuskWarning && IsPreNight == false
						&& RemainingPhaseSeconds <= PreNightLeadSeconds)
						IsPreNight = true;
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
						ReturnEveryoneToLobbyScene();
					break;
			}
		}

		/// <summary>
		/// Host-only readiness gate. The game scene starts in <see cref="MatchPhase.Lobby"/>; once every connected
		/// player has finished loading and spawned a <see cref="Player"/>, lock in the lobby's team-size choice and
		/// kick off the round. ("Ready" == "finished loading" — there is no explicit per-player ready toggle.)
		/// </summary>
		private void TryAutoBegin()
		{
			if (_gameManager == null)
				_gameManager = GameManager.Instance;
			if (_gameManager == null) return;

			int expected = Runner.ActivePlayers.Count();
			if (expected <= 0) return; // no humans connected yet

			int spawnedHumans = 0;
			var players = _gameManager.Players;
			for (int i = 0; i < players.Count; i++)
			{
				var p = players[i];
				if (p == null || p.Object == null || p.IsBot) continue;
				spawnedHumans++;
			}
			if (spawnedHumans < expected) return; // still waiting for everyone to load in

			if (TeamManager != null)
				TeamManager.SetTeamSize(MatchBootstrap.PendingTeamSize);

			BeginMatch();
		}

		/// <summary>Host-only: clear team data and send everyone back to the lobby scene. Clients follow the host's
		/// scene change automatically; this MatchManager despawns with the game scene.</summary>
		private void ReturnEveryoneToLobbyScene()
		{
			if (_returningToLobbyScene) return;
			_returningToLobbyScene = true;

			if (TeamManager != null)
				TeamManager.ClearAssignments();

			Runner.LoadScene(SceneRef.FromIndex(LobbySceneIndex), LoadSceneMode.Single, LocalPhysicsMode.None, true);
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

		/// <summary>Force-returns everyone to the lobby scene (debug / admin). State-authority only.</summary>
		public void ReturnToLobby()
		{
			if (HasStateAuthority == false) return;
			WinningTeamId = -1;
			ReturnEveryoneToLobbyScene();
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

		/// <summary>Debug-only: fast-forward the <see cref="MatchPhase.Day"/> phase so only <paramref name="leadSeconds"/>
		/// remain before <see cref="MatchPhase.DuskWarning"/> (the first siren). Crucially leaves the phase machine
		/// <em>running</em> (clears any <c>set_dusk</c>/<c>set_nighttime</c> hold) so it advances into DuskWarning on its own
		/// — letting you watch the siren sound and NPCs retreat home in real time, instead of snapping straight to the phase.
		/// If not currently in Day, forces Day first so the countdown is meaningful. Routes to the state authority.</summary>
		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_DebugSkipToDusk(float leadSeconds)
		{
			if (HasStateAuthority == false) return;

			_debugHoldPhase = false; // let the machine auto-advance Day → DuskWarning naturally
			if (Phase != MatchPhase.Day)
			{
				WinningTeamId = -1;
				EnterPhase(MatchPhase.Day);
			}
			PhaseTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.1f, leadSeconds));
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

		/// <summary>Debug-only: set the networked game speed from any peer. Routes to the state authority, which writes
		/// <see cref="DebugTimeScale"/>; every peer then applies it to its local <see cref="Time.timeScale"/> via
		/// <see cref="OnTimeScaleChangedRender"/>. Clamped to ≥ 0 (0 = paused).</summary>
		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_DebugSetTimeScale(float scale)
		{
			if (HasStateAuthority == false) return;
			DebugTimeScale = Mathf.Max(0f, scale);
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
			var gm = GameManager.Instance;
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
			// Every transition clears the PreNight beat — entering Night this way is what brings the lights back on.
			IsPreNight = false;
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

		private void OnPreNightChangedRender()
		{
			PreNightChanged?.Invoke(IsPreNight);
		}

		/// <summary>Applies the replicated <see cref="DebugTimeScale"/> to this peer's local game speed.</summary>
		private void OnTimeScaleChangedRender()
		{
			Time.timeScale = Mathf.Max(0f, DebugTimeScale);
		}

		// =========================================================================
		// Read helpers (UI, aesthetics)
		// =========================================================================

		/// <summary>Seconds remaining in the current phase, or 0 if no timer (Lobby).</summary>
		public float RemainingPhaseSeconds => PhaseTimer.RemainingTime(Runner).GetValueOrDefault();

		public bool IsPvpActive => Phase == MatchPhase.Night;
	}
}
