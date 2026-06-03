using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>Lifecycle of a household across the match phases.</summary>
	public enum BuildingState : byte
	{
		/// <summary>Day/Lobby — exits open, members do their normal passive routine.</summary>
		Open = 0,
		/// <summary>DuskWarning — siren sounded, members are hurrying home and shutting exits.</summary>
		Lockdown = 1,
		/// <summary>All exits closed/breached — members have buttoned up and wander the interior.</summary>
		Sealed = 2,
		/// <summary>Night — the household is hostile and defends its zone.</summary>
		Hostile = 3,
	}

	/// <summary>
	/// A town building that owns a group of <see cref="NpcAgent"/> townsfolk, the doorways they shut
	/// (<see cref="NpcExit"/>), and the areas they live in. Hand-authored in the scene: drag the member
	/// NPCs' <see cref="NpcAgent.Building"/> ref to this, list the exits, and place the interior/defense
	/// areas + retreat anchors.
	///
	/// It is the phase brain for its household. Reading <see cref="MatchManager.PhaseChanged"/> on the
	/// state authority it walks <see cref="BuildingState"/> Open → Lockdown → Sealed → Hostile and pushes
	/// each transition to its members (retreat at dusk, defend at night, resume by day). The
	/// <see cref="State"/> itself is networked so cosmetic peers (lights, ambience) can react via
	/// <see cref="StateChanged"/>; all member/exit orchestration runs authority-only, since NPC AI does.
	/// </summary>
	public sealed class NpcBuilding : NetworkBehaviour
	{
		[Header("Areas")]
		[Tooltip("Box the household wanders inside once home (and the day-vs-night interior).")]
		public NpcMovementArea InteriorArea;
		[Tooltip("Larger box = 'their zone or too close to it'. Players inside it at night are attacked; chase is leashed to it. Falls back to InteriorArea when unset.")]
		public NpcMovementArea DefenseArea;

		[Header("Exits")]
		[Tooltip("Every doorway the household shuts during lockdown. Players force these to get inside.")]
		public List<NpcExit> Exits = new List<NpcExit>();

		[Header("Interior retreat anchors")]
		[Tooltip("Distinct standing points inside, assigned to members round-robin so they don't stack on one spot. Falls back to the interior centre.")]
		public Transform[] InteriorAnchors;

		/// <summary>Current household state. State authority writes; replicated for cosmetic peers.</summary>
		[Networked, OnChangedRender(nameof(OnStateChangedRender))]
		public BuildingState State { get; private set; }

		/// <summary>Raised on every peer when a building's <see cref="State"/> changes (drive lights/ambience off this).</summary>
		public static event Action<NpcBuilding, BuildingState> StateChanged;

		public bool IsHostile => State == BuildingState.Hostile;

		/// <summary>The zone used for night aggro and chase-leashing — the defense box, or the interior if none is set.</summary>
		public NpcMovementArea DefenseZone => DefenseArea != null ? DefenseArea : InteriorArea;

		// Authority-local: NPC AI only ever runs on the state authority, so members register there.
		private readonly List<NpcAgent> _members = new List<NpcAgent>();

		public override void Spawned()
		{
			MatchManager.PhaseChanged += OnPhaseChanged;
			if (HasStateAuthority)
				ApplyPhase(MatchManager.Instance != null ? MatchManager.Instance.Phase : MatchPhase.Lobby);
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			MatchManager.PhaseChanged -= OnPhaseChanged;
		}

		// ─── Membership (authority-only) ──────────────────────────────────────────

		/// <summary>An NPC joins its household (called from <see cref="NpcAgent.Spawned"/>). If it spawned
		/// mid-phase it immediately adopts the household's current behavior.</summary>
		public void RegisterMember(NpcAgent agent)
		{
			if (agent == null || _members.Contains(agent)) return;
			_members.Add(agent);

			switch (State)
			{
				case BuildingState.Lockdown:
				case BuildingState.Sealed:  agent.BeginRetreat();       break;
				case BuildingState.Hostile: agent.BeginHostileGuard();  break;
			}
		}

		public void UnregisterMember(NpcAgent agent) => _members.Remove(agent);

		// ─── Exit assignment / interior anchors ───────────────────────────────────

		/// <summary>Reserve the nearest still-open, unclaimed exit for <paramref name="agent"/> to shut.
		/// Returns null when every exit is already claimed, closed, or breached.</summary>
		public NpcExit ClaimNextOpenExit(NpcAgent agent)
		{
			NpcExit best   = null;
			float bestSqr  = float.MaxValue;
			for (int i = 0; i < Exits.Count; i++)
			{
				var e = Exits[i];
				if (e == null || e.CanBeClaimed == false) continue;
				float sqr = (e.CloseStandPoint - agent.transform.position).sqrMagnitude;
				if (sqr < bestSqr) { best = e; bestSqr = sqr; }
			}
			if (best != null && best.TryClaim(agent)) return best;
			return null;
		}

		/// <summary>An interior standing point for <paramref name="agent"/> to settle at, spread across
		/// <see cref="InteriorAnchors"/> by membership order.</summary>
		public Vector3 GetRetreatPoint(NpcAgent agent)
		{
			if (InteriorAnchors != null && InteriorAnchors.Length > 0)
			{
				int idx = _members.IndexOf(agent);
				if (idx < 0) idx = 0;
				var t = InteriorAnchors[idx % InteriorAnchors.Length];
				if (t != null) return t.position;
			}
			if (InteriorArea != null) return InteriorArea.Center;
			return transform.position;
		}

		// ─── Phase machine (authority-only) ───────────────────────────────────────

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false) return;

			// Promote Lockdown → Sealed once every doorway is shut or breached (cosmetic "buttoned up").
			if (State == BuildingState.Lockdown && AllExitsSecured())
				State = BuildingState.Sealed;
		}

		private void OnPhaseChanged(MatchPhase phase)
		{
			if (HasStateAuthority == false) return;
			ApplyPhase(phase);
		}

		private void ApplyPhase(MatchPhase phase)
		{
			switch (phase)
			{
				case MatchPhase.Lobby:
				case MatchPhase.Day:         OpenUp();        break;
				case MatchPhase.DuskWarning: BeginLockdown(); break;
				case MatchPhase.Night:       GoHostile();     break;
				case MatchPhase.MatchOver:                    break; // round ending — leave the household as-is
			}
		}

		private void OpenUp()
		{
			State = BuildingState.Open;
			for (int i = 0; i < Exits.Count; i++)
				if (Exits[i] != null) Exits[i].AuthorityResetForDay();
			for (int i = 0; i < _members.Count; i++)
				if (_members[i] != null) _members[i].ResumePassive();
		}

		private void BeginLockdown()
		{
			State = BuildingState.Lockdown;
			for (int i = 0; i < _members.Count; i++)
				if (_members[i] != null) _members[i].BeginRetreat();
		}

		private void GoHostile()
		{
			State = BuildingState.Hostile;
			for (int i = 0; i < _members.Count; i++)
				if (_members[i] != null) _members[i].BeginHostileGuard();
		}

		private bool AllExitsSecured()
		{
			for (int i = 0; i < Exits.Count; i++)
			{
				var e = Exits[i];
				if (e == null) continue;
				if (e.IsClosed == false && e.IsBreached == false) return false;
			}
			return true;
		}

		private void OnStateChangedRender() => StateChanged?.Invoke(this, State);

		// ─── Gizmos ───────────────────────────────────────────────────────────────

		private void OnDrawGizmosSelected()
		{
			if (InteriorArea != null)
			{
				Gizmos.color = new Color(0.3f, 1f, 0.4f, 1f);
				DrawAreaGizmo(InteriorArea);
			}
			if (DefenseArea != null)
			{
				Gizmos.color = new Color(1f, 0.45f, 0.2f, 1f);
				DrawAreaGizmo(DefenseArea);
			}

			Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
			if (Exits != null)
				for (int i = 0; i < Exits.Count; i++)
					if (Exits[i] != null) Gizmos.DrawLine(transform.position, Exits[i].transform.position);
		}

		private static void DrawAreaGizmo(NpcMovementArea area)
		{
			Gizmos.matrix = Matrix4x4.TRS(area.transform.position, area.transform.rotation, Vector3.one);
			Gizmos.DrawWireCube(Vector3.zero, area.Size);
			Gizmos.matrix = Matrix4x4.identity;
		}
	}
}
