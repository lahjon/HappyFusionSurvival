using Fusion;
using Starter.Common.Interactions;
using UnityEngine;
using UnityEngine.AI;

namespace Starter.Shooter
{
	/// <summary>
	/// A closeable doorway belonging to an <c>NpcBuilding</c>. During the dusk lockdown an
	/// <see cref="NpcAgent"/> paths to <see cref="CloseStandPoint"/> and shuts it. A closed exit
	/// blocks player movement (<see cref="BlockingCollider"/>) and NPC navigation (the
	/// <see cref="NavObstacle"/> carves the doorway out of the navmesh).
	///
	/// Players force entry two ways, both of which permanently breach the exit for the round:
	///  • smash it — the door carries a <see cref="Health"/> sibling that normal combat damages
	///    (non-player target, so the PvP gate always lets the hits through); HP at zero → breach.
	///  • pry it — hold Interact for <see cref="PryHoldSeconds"/> (the shared interaction system).
	///
	/// Networking: open/closed + breached state is state-authority-written and replicated via
	/// OnChangedRender, so every peer sees the same door pose, collider, and obstacle. The
	/// closer-claim bookkeeping is authority-local because only the state authority simulates AI.
	/// </summary>
	[RequireComponent(typeof(Health))]
	public sealed class NpcExit : NetworkBehaviour, IInteractable, IHoldInteractable
	{
		[Header("Door visual")]
		[Tooltip("Pivot rotated between the open and closed angles. Defaults to this transform.")]
		public Transform DoorPivot;
		[Tooltip("Local Y rotation (degrees) when shut.")]
		public float ClosedYAngle = 0f;
		[Tooltip("Local Y rotation (degrees) when open.")]
		public float OpenYAngle = 90f;
		[Tooltip("Optional object shown once the door is smashed or pried (splinters, broken frame).")]
		public GameObject BrokenVisual;
		[Tooltip("Seconds the door takes to swing between open and closed (visual only).")]
		[Min(0f)] public float SwingSeconds = 0.35f;

		[Header("Blocking")]
		[Tooltip("Collider that blocks player movement while closed. Enabled when shut, disabled when open.")]
		public Collider BlockingCollider;
		[Tooltip("NavMesh obstacle that carves the doorway out of the navmesh while shut.")]
		public NavMeshObstacle NavObstacle;

		[Header("Where the NPC stands to close it")]
		[Tooltip("Point an NPC navigates to in order to shut this exit. Defaults to this transform.")]
		public Transform CloseTransform;

		[Header("Interaction (pry open)")]
		[Tooltip("Seconds a player must hold Interact to pry a shut door open without smashing it.")]
		[Min(0.1f)] public float PryHoldSeconds = 2.5f;
		[Tooltip("Range at which a player can pry this exit.")]
		[Min(0.1f)] public float InteractRangeValue = 2.5f;

		/// <summary>True while the door is shut. State authority writes; replicated.</summary>
		[Networked, OnChangedRender(nameof(OnStateChangedRender))]
		public NetworkBool IsClosed { get; private set; }

		/// <summary>True once forced open (smashed or pried). A breached exit cannot be re-closed this round.</summary>
		[Networked, OnChangedRender(nameof(OnStateChangedRender))]
		public NetworkBool IsBreached { get; private set; }

		public bool IsOpen => IsClosed == false;

		private Health _health;
		private float _swingVel; // SmoothDamp scratch for the visual swing.

		// Authority-local: which NPC has taken the job of closing this exit (stops the whole
		// household converging on one door). Only the state authority runs AI, so it needs no replication.
		private NpcAgent _claimedBy;

		/// <summary>World point an NPC should stand at to shut this exit.</summary>
		public Vector3 CloseStandPoint => CloseTransform != null ? CloseTransform.position : transform.position;

		private void Awake()
		{
			_health = GetComponent<Health>();
			if (DoorPivot == null)      DoorPivot = transform;
			if (CloseTransform == null) CloseTransform = transform;
		}

		public override void Spawned()
		{
			// The door drives its own visual; suppress Health's default VisualRoot/DeathRoot swap
			// (which would also NRE here since this object wires neither).
			if (_health != null) _health.SuppressDeathVisualSwap = true;
			ApplyState(snap: true);
		}

		// ─── Authority API (called by NpcBuilding / NpcAgent) ─────────────────────

		/// <summary>State authority: shut the exit. No-op once breached this round.</summary>
		public void AuthorityClose()
		{
			if (HasStateAuthority == false) return;
			if (IsBreached) return;
			IsClosed = true;
		}

		/// <summary>State authority: reset for a new round — reopen, clear the breach, restore door HP.</summary>
		public void AuthorityResetForDay()
		{
			if (HasStateAuthority == false) return;
			IsClosed   = false;
			IsBreached = false;
			_claimedBy = null;
			if (_health != null) _health.Revive();
		}

		// ─── Closer claim (authority-local) ───────────────────────────────────────

		public bool IsClaimed   => _claimedBy != null;
		public bool CanBeClaimed => IsClosed == false && IsBreached == false && _claimedBy == null;

		/// <summary>Authority-only: try to reserve this exit for <paramref name="agent"/> to close.</summary>
		public bool TryClaim(NpcAgent agent)
		{
			if (CanBeClaimed == false) return false;
			_claimedBy = agent;
			return true;
		}

		/// <summary>Authority-only: release a claim (the NPC gave up or finished). Idempotent.</summary>
		public void ReleaseClaim(NpcAgent agent)
		{
			if (_claimedBy == agent) _claimedBy = null;
		}

		// ─── Breach detection ─────────────────────────────────────────────────────

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false) return;

			// Door HP hit zero (a player smashed it) → permanent breach for the round.
			if (IsClosed && IsBreached == false && _health != null && _health.IsAlive == false)
				Breach();
		}

		private void Breach()
		{
			IsBreached = true;
			IsClosed   = false;
			_claimedBy = null;
		}

		// ─── IInteractable / IHoldInteractable (pry open) ─────────────────────────

		public float InteractRange => InteractRangeValue;
		// Only a shut, un-breached door can be pried.
		public bool CanInteract => IsClosed && IsBreached == false;
		public Vector3 InteractionPoint => transform.position;
		public string LockedReason => string.Empty;
		public string InteractLabel => "Pry open";

		// Hold-only: the tap path does nothing.
		public void OnInteract(InteractionScanner scanner) { }

		public float HoldSeconds => PryHoldSeconds;
		public void LocalRequestHoldStart()  { }
		public void LocalRequestHoldCancel() { }
		public void LocalRequestHoldComplete() => RPC_RequestPry();

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_RequestPry(RpcInfo info = default)
		{
			if (HasStateAuthority == false) return;
			if (IsClosed == false || IsBreached) return;

			// Re-validate range on the host — never trust the client scan alone (convention: range * 1.25).
			var playerObj = Runner.GetPlayerObject(info.Source);
			if (playerObj != null)
			{
				float r = InteractRangeValue * 1.25f;
				if ((playerObj.transform.position - InteractionPoint).sqrMagnitude > r * r) return;
			}

			Breach();
		}

		// ─── Replicated visual / blocking state ───────────────────────────────────

		private void OnStateChangedRender() => ApplyState(snap: false);

		/// <summary>
		/// Applies the networked open/closed/breached state to local visuals and blockers on every peer.
		/// Collider, navmesh obstacle, broken visual, and Health damageability snap immediately; the door
		/// pivot eases toward its target angle in <see cref="Update"/> unless <paramref name="snap"/> is set.
		/// </summary>
		private void ApplyState(bool snap)
		{
			bool closed = IsClosed; // Breach already forces IsClosed false.

			if (BlockingCollider != null) BlockingCollider.enabled = closed;
			if (NavObstacle != null)      NavObstacle.enabled      = closed;
			if (BrokenVisual != null)     BrokenVisual.SetActive(IsBreached);

			// Only damageable while shut — an open/breached doorway has nothing left to smash.
			// IsInvulnerable is non-networked, so set it on every peer that calls TakeHit (all of them).
			if (_health != null) _health.IsInvulnerable = closed == false;

			if (snap && DoorPivot != null)
			{
				Vector3 e = DoorPivot.localEulerAngles;
				e.y = closed ? ClosedYAngle : OpenYAngle;
				DoorPivot.localEulerAngles = e;
				_swingVel = 0f;
			}
		}

		private void Update()
		{
			if (DoorPivot == null) return;
			// [Networked] properties are only valid after Spawned() — guard against Unity's
			// Update() firing before Fusion has initialised this object's state snapshot.
			if (Object == null || Object.IsValid == false) return;

			float targetY  = IsClosed ? ClosedYAngle : OpenYAngle;
			float currentY = DoorPivot.localEulerAngles.y;
			float nextY    = SwingSeconds <= 0f
				? targetY
				: Mathf.SmoothDampAngle(currentY, targetY, ref _swingVel, SwingSeconds);

			Vector3 e = DoorPivot.localEulerAngles;
			e.y = nextY;
			DoorPivot.localEulerAngles = e;
		}
	}
}
