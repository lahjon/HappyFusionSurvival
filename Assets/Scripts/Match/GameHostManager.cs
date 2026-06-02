using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Host-authoritative director for dynamic Night-phase events (the Purge "director"). It owns a list of
	/// <see cref="MatchEvent"/>s, runs them while <see cref="MatchManager.Phase"/> is <see cref="MatchPhase.Night"/>,
	/// and provides the shared services events build on — roster queries, passivity scoring, team purge.
	///
	/// <para><b>Networked-first, host-owns-the-process.</b> Passivity scoring is intentionally <i>not</i> networked: it
	/// is pure host-side bookkeeping (per-player activity timestamps refreshed from movement + combat). The only state
	/// pushed to clients is the generic active-event slot — <see cref="ActiveEvent"/> + <see cref="EventTargetTeam"/> +
	/// <see cref="EventTimer"/> — which UI reads to draw whatever is happening. Each event maps its client-relevant
	/// state into that fixed slot via <see cref="PublishEvent"/>, so adding event types never changes the wire schema.</para>
	///
	/// <para>To add an event: subclass <see cref="MatchEvent"/>, add a <see cref="MatchEventType"/> value, and register
	/// an instance in <see cref="BuildEvents"/>. See <see cref="TheHunt"/> for the canonical example.</para>
	///
	/// Scene-placed sibling of <see cref="MatchManager"/> / <see cref="TeamManager"/> — on the same NetworkObject.
	/// </summary>
	public sealed class GameHostManager : NetworkBehaviour
	{
		public static GameHostManager Instance { get; private set; }

		/// <summary>Fires on every peer when the active event changes (None = nothing running). Late-joiners replay the
		/// current value on Spawned, so subscribers must be idempotent.</summary>
		public static event Action<MatchEventType> ActiveEventChanged;

		[Header("Director")]
		[Tooltip("Grace period after Night begins before any event may auto-trigger — lets players engage first.")]
		[Min(0f)] public float EventWarmupSeconds = 30f;

		[Tooltip("Cooldown after an event resolves before another can auto-trigger.")]
		[Min(0f)] public float EventCooldownSeconds = 30f;

		[Header("Random director beats")]
		[Tooltip("Director fires weighted-random events (Blackout/Hunt) at random intervals through the Night.")]
		public bool EnableRandomBeats = true;

		[Tooltip("Minimum seconds between random director beats.")]
		[Min(5f)] public float MinBeatInterval = 60f;

		[Tooltip("Maximum seconds between random director beats.")]
		[Min(5f)] public float MaxBeatInterval = 120f;

		[Header("Passivity detection")]
		[Tooltip("A living team is an event candidate once even its most-active member has been idle this long.")]
		[Min(5f)] public float PassivitySeconds = 45f;

		[Tooltip("Metres a player must move between samples to count as 'active'. Below this counts as idle.")]
		[Min(0f)] public float ActiveMoveDistance = 3f;

		[Tooltip("Seconds between host activity samples. Cheap — only the host runs it, capped at 18 players.")]
		[Min(0.1f)] public float SampleInterval = 1f;

		[Header("The Hunt")]
		[Tooltip("Time the hunter team has to eliminate a rival team before being purged. Default: 2 minutes.")]
		[Min(5f)] public float HuntDurationSeconds = 2f * 60f;

		[Header("Blackout")]
		[Tooltip("How long the town stays fully dark before power is restored. Default: 30s.")]
		[Min(1f)] public float BlackoutDurationSeconds = 30f;

		// =========================================================================
		// Networked state — the ONLY thing clients read. Fixed schema shared by all events.
		// =========================================================================

		/// <summary>Which event is active, or <see cref="MatchEventType.None"/>. State authority only writes.</summary>
		[Networked, OnChangedRender(nameof(OnActiveEventChangedRender))]
		public MatchEventType ActiveEvent { get; private set; }

		/// <summary>Event-specific target team (e.g. the hunter team for <see cref="TheHunt"/>), or -1 if not applicable.</summary>
		[Networked] public int EventTargetTeam { get; private set; } = -1;

		/// <summary>Counts down the active event. Clients read <see cref="RemainingEventSeconds"/> for UI.</summary>
		[Networked] public TickTimer EventTimer { get; private set; }

		// =========================================================================
		// Host-local bookkeeping (never networked)
		// =========================================================================

		// Tick of each player's last "meaningful" action (movement past threshold or PvP damage dealt).
		private readonly Dictionary<PlayerRef, int> _lastActivityTick = new(TeamManager.MaxPlayers);
		// Last sampled position per player, to measure movement between samples.
		private readonly Dictionary<PlayerRef, Vector3> _lastSampledPos = new(TeamManager.MaxPlayers);

		private readonly List<MatchEvent> _events = new();
		private MatchEvent _activeEvent;     // currently running event, or null

		private int _nextSampleTick;
		private int _eventReadyTick;         // earliest tick an event may auto-trigger again (warmup / cooldown)
		private int _nextBeatTick;           // earliest tick the next random beat may fire
		private MatchEventType _lastBeatType = MatchEventType.None; // last beat-fired event, for the repeat penalty
		private bool _wasNight;

		private MatchManager _match;
		private TeamManager _teams;
		private GameManager _gameManager;

		// =========================================================================
		// Lifecycle
		// =========================================================================

		public override void Spawned()
		{
			Instance = this;

			_match = MatchManager.Instance != null ? MatchManager.Instance : GetComponent<MatchManager>();
			if (_match == null) _match = FindAnyObjectByType<MatchManager>();

			_teams = TeamManager.Instance != null ? TeamManager.Instance : GetComponent<TeamManager>();
			if (_teams == null) _teams = FindAnyObjectByType<TeamManager>();

			_gameManager = FindAnyObjectByType<GameManager>();

			BuildEvents();

			// Late-joiners replay the current event so UI can wire up.
			ActiveEventChanged?.Invoke(ActiveEvent);
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (Instance == this) Instance = null;
		}

		/// <summary>Register the event roster. Order is the auto-trigger priority — earlier events get first refusal
		/// each tick. Add new <see cref="MatchEvent"/> subclasses here.</summary>
		private void BuildEvents()
		{
			_events.Clear();

			Register(new TheHunt());
			Register(new Blackout());
		}

		private void Register(MatchEvent ev)
		{
			ev.Bind(this);
			_events.Add(ev);
		}

		// =========================================================================
		// Director loop (host only)
		// =========================================================================

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false) return;

			bool isNight = _match != null && _match.Phase == MatchPhase.Night;

			if (isNight == false)
			{
				if (_wasNight)
				{
					// Left Night (match over / forced day) — cancel any in-flight event and reset tracking.
					if (_activeEvent != null) _activeEvent.Resolve();
					_lastActivityTick.Clear();
					_lastSampledPos.Clear();
					_wasNight = false;
				}
				return;
			}

			int tick = Runner.Tick;

			if (_wasNight == false)
			{
				BeginNightTracking(tick);
				_wasNight = true;
			}

			if (tick >= _nextSampleTick)
			{
				SampleActivity(tick);
				_nextSampleTick = tick + SecondsToTicks(SampleInterval);
			}

			if (_activeEvent != null)
			{
				_activeEvent.Tick(tick);   // may Resolve() itself, clearing _activeEvent via NotifyEventResolved
			}
			else if (tick >= _eventReadyTick)
			{
				// Reactive pass first (e.g. passivity → The Hunt). If nothing reactive fired, roll a random beat.
				if (TryReactiveTrigger(tick) == false && EnableRandomBeats && tick >= _nextBeatTick)
				{
					TriggerRandomBeat(tick);
					ScheduleNextBeat(tick);
				}
			}
		}

		// Give each event a chance to auto-trigger via its own heuristic (forced events stay dormant here).
		private bool TryReactiveTrigger(int tick)
		{
			for (int i = 0; i < _events.Count; i++)
			{
				if (_events[i].CanTrigger(tick))
				{
					StartEvent(_events[i], tick);
					return true;
				}
			}
			return false;
		}

		// Pick a weighted-random beat-eligible event and fire it. A repeat penalty discourages (but doesn't forbid)
		// firing the same event twice in a row when alternatives exist.
		private void TriggerRandomBeat(int tick)
		{
			const float repeatPenalty = 0.25f;

			float total = 0f;
			for (int i = 0; i < _events.Count; i++)
			{
				float w = _events[i].RandomBeatWeight;
				if (w <= 0f) continue;
				if (_events[i].Type == _lastBeatType) w *= repeatPenalty;
				total += w;
			}
			if (total <= 0f) return;

			float roll = UnityEngine.Random.value * total;
			MatchEvent chosen = null;
			for (int i = 0; i < _events.Count; i++)
			{
				float w = _events[i].RandomBeatWeight;
				if (w <= 0f) continue;
				if (_events[i].Type == _lastBeatType) w *= repeatPenalty;

				roll -= w;
				if (roll <= 0f) { chosen = _events[i]; break; }
			}
			if (chosen == null) return;

			// Force it through its normal trigger path; auto-pick params (team/zone) since beats are unparameterised.
			chosen.RequestForce();
			if (chosen.CanTrigger(tick))
			{
				StartEvent(chosen, tick);
				_lastBeatType = chosen.Type;
			}
		}

		private void ScheduleNextBeat(int tick)
		{
			float min = Mathf.Min(MinBeatInterval, MaxBeatInterval);
			float max = Mathf.Max(MinBeatInterval, MaxBeatInterval);
			_nextBeatTick = tick + SecondsToTicks(UnityEngine.Random.Range(min, max));
		}

		private void StartEvent(MatchEvent ev, int tick)
		{
			_activeEvent = ev;
			ev.Begin(tick);
		}

		private void BeginNightTracking(int tick)
		{
			_eventReadyTick = tick + SecondsToTicks(EventWarmupSeconds);
			_nextSampleTick = tick;
			_lastBeatType = MatchEventType.None;
			ScheduleNextBeat(tick);   // first beat lands ~one interval into the night (after warmup gates it)
			_lastActivityTick.Clear();
			_lastSampledPos.Clear();

			foreach (var p in LivingPlayers())
			{
				var pref = p.Owner;
				_lastActivityTick[pref] = tick;
				_lastSampledPos[pref] = p.KCC.Position;
			}
		}

		// Refresh activity from movement. Combat activity arrives separately via RecordCombat().
		private void SampleActivity(int tick)
		{
			foreach (var p in LivingPlayers())
			{
				var pref = p.Owner;
				Vector3 pos = p.KCC.Position;

				if (_lastSampledPos.TryGetValue(pref, out var prev))
				{
					if ((pos - prev).sqrMagnitude >= ActiveMoveDistance * ActiveMoveDistance)
						_lastActivityTick[pref] = tick;
				}
				else
				{
					// First time we see this player (late joiner) — treat as active baseline.
					_lastActivityTick[pref] = tick;
				}

				_lastSampledPos[pref] = pos;
			}
		}

		/// <summary>Called by <see cref="Health.TakeHit"/> on the state authority when a player lands PvP damage during
		/// Night. Counts as activity for the attacker so aggressive players are never tagged as passive.</summary>
		public void RecordCombat(PlayerRef attacker)
		{
			if (HasStateAuthority == false) return;
			if (attacker == PlayerRef.None) return;
			_lastActivityTick[attacker] = Runner.Tick;
		}

		// =========================================================================
		// Event publishing (called BY events, host only)
		// =========================================================================

		/// <summary>An active event publishes its client-visible state into the shared networked slot. Called from a
		/// <see cref="MatchEvent.OnStart"/> implementation. <paramref name="targetTeam"/> / <paramref name="zone"/> are
		/// optional event-specific payloads (-1 when unused).</summary>
		internal void PublishEvent(MatchEventType type, float durationSeconds, int targetTeam = -1)
		{
			ActiveEvent = type;
			EventTargetTeam = targetTeam;
			EventTimer = TickTimer.CreateFromSeconds(Runner, durationSeconds);
		}

		/// <summary>Called by <see cref="MatchEvent.Resolve"/> when an event ends. Clears the networked slot and starts
		/// the inter-event cooldown.</summary>
		internal void NotifyEventResolved(MatchEvent ev)
		{
			if (_activeEvent == ev) _activeEvent = null;

			// Fusion only fires OnChangedRender when the value actually changes, so unconditional writes are safe.
			ActiveEvent = MatchEventType.None;
			EventTargetTeam = -1;
			EventTimer = default;
			_eventReadyTick = Runner.Tick + SecondsToTicks(EventCooldownSeconds);

			// Space the next random beat from the event's end (cooldown gates it regardless).
			ScheduleNextBeat(Runner.Tick);
		}

		// =========================================================================
		// Manual / debug control
		// =========================================================================

		/// <summary>Force "The Hunt" NOW (bypassing warmup/cooldown). teamId &lt; 0 auto-picks the most passive team.</summary>
		public void ForceHunt(int teamId)
		{
			if (FindEvent(MatchEventType.Hunt) is TheHunt hunt)
			{
				hunt.RequestForce(teamId);
				TryStartForced(hunt);
			}
		}

		/// <summary>Force a town-wide Blackout NOW.</summary>
		public void ForceBlackout()
		{
			var ev = FindEvent(MatchEventType.Blackout);
			if (ev == null) return;
			ev.RequestForce();
			TryStartForced(ev);
		}

		// Shared force path: start the event immediately if Night is running and nothing else is active.
		private void TryStartForced(MatchEvent ev)
		{
			if (HasStateAuthority == false || ev == null) return;
			if (_match == null || _match.Phase != MatchPhase.Night) return;
			if (_activeEvent != null) return;

			int tick = Runner.Tick;
			if (ev.CanTrigger(tick))
				StartEvent(ev, tick);
		}

		private MatchEvent FindEvent(MatchEventType type)
		{
			for (int i = 0; i < _events.Count; i++)
				if (_events[i].Type == type) return _events[i];
			return null;
		}

		// ---- Debug entry points (route from any peer to the state authority) ----

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_DebugForceHunt(int teamId)
		{
			if (HasStateAuthority) ForceHunt(teamId);
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_DebugForceBlackout()
		{
			if (HasStateAuthority) ForceBlackout();
		}

		// =========================================================================
		// Shared services for events (host only) — roster + passivity + purge
		// =========================================================================

		public IEnumerable<Player> LivingPlayers()
		{
			var players = _gameManager != null ? _gameManager.Players : null;
			if (players == null) yield break;

			for (int i = 0; i < players.Count; i++)
			{
				var p = players[i];
				if (p == null || p.Object == null) continue;
				if (p.Health == null || p.Health.IsAlive == false) continue;
				yield return p;
			}
		}

		public int TeamOf(Player p)
		{
			if (p == null || p.Object == null || _teams == null) return -1;
			return _teams.TeamOf(p.Owner);
		}

		public bool TeamIsLiving(int teamId)
		{
			if (teamId < 0) return false;
			foreach (var p in LivingPlayers())
			{
				if (TeamOf(p) == teamId)
					return true;
			}
			return false;
		}

		public int CountLivingTeams()
		{
			Span<bool> seen = stackalloc bool[TeamManager.MaxPlayers];
			int count = 0;
			foreach (var p in LivingPlayers())
			{
				int t = TeamOf(p);
				if (t < 0 || t >= TeamManager.MaxPlayers || seen[t]) continue;
				seen[t] = true;
				count++;
			}
			return count;
		}

		/// <summary>Kill every living member of a team (e.g. a hunter that failed its contract). State-authority only.</summary>
		public void PurgeTeam(int teamId)
		{
			if (teamId < 0) return;
			foreach (var p in LivingPlayers())
			{
				if (TeamOf(p) == teamId)
					p.Health.AuthorityKill();
			}
		}

		/// <summary>Returns the living team whose <i>most-active</i> member has been idle the longest (a team only counts
		/// as passive when all its members are idle). <paramref name="idleTicks"/> is that team's idle duration in ticks;
		/// <paramref name="livingTeams"/> is the count of teams with a living member. Returns -1 if none qualify.</summary>
		public int MostPassiveLivingTeam(int tick, out int idleTicks, out int livingTeams)
		{
			idleTicks = 0;
			livingTeams = 0;

			// Per-team: smallest idle gap among living members (how active the team's best player is) and a living flag.
			Span<int> teamBestIdle = stackalloc int[TeamManager.MaxPlayers];
			Span<bool> teamLiving = stackalloc bool[TeamManager.MaxPlayers];
			for (int i = 0; i < teamBestIdle.Length; i++) teamBestIdle[i] = int.MaxValue;

			foreach (var p in LivingPlayers())
			{
				int t = TeamOf(p);
				if (t < 0 || t >= TeamManager.MaxPlayers) continue;

				teamLiving[t] = true;

				var pref = p.Owner;
				int last = _lastActivityTick.TryGetValue(pref, out int v) ? v : tick;
				int idle = tick - last;
				if (idle < teamBestIdle[t]) teamBestIdle[t] = idle;
			}

			int bestTeam = -1;
			int bestIdle = -1;
			for (int t = 0; t < TeamManager.MaxPlayers; t++)
			{
				if (teamLiving[t] == false) continue;
				livingTeams++;

				int teamIdle = teamBestIdle[t] == int.MaxValue ? 0 : teamBestIdle[t];
				if (teamIdle > bestIdle)
				{
					bestIdle = teamIdle;
					bestTeam = t;
				}
			}

			idleTicks = Mathf.Max(0, bestIdle);
			return bestTeam;
		}

		public int SecondsToTicks(float seconds) => Mathf.Max(1, Mathf.RoundToInt(seconds * Runner.TickRate));

		// =========================================================================
		// Read helpers (UI / callers)
		// =========================================================================

		/// <summary>True while "The Hunt" is the active event.</summary>
		public bool IsHuntActive => ActiveEvent == MatchEventType.Hunt;

		/// <summary>True while an event is active and <paramref name="teamId"/> is its target team (e.g. the hunter).</summary>
		public bool IsEventTarget(int teamId) => ActiveEvent != MatchEventType.None && teamId >= 0 && teamId == EventTargetTeam;

		/// <summary>Seconds left in the active event, or 0 when none is running.</summary>
		public float RemainingEventSeconds => ActiveEvent != MatchEventType.None ? EventTimer.RemainingTime(Runner).GetValueOrDefault() : 0f;

		// =========================================================================
		// Internals
		// =========================================================================

		private void OnActiveEventChangedRender()
		{
			ActiveEventChanged?.Invoke(ActiveEvent);
		}
	}
}
