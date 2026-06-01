using Fusion;
using UnityEngine;

namespace Starter.Common.Interactions
{
	/// <summary>
	/// Generic, mode-agnostic "play animation on interact" capability. Holds a single
	/// networked open/closed <see cref="State"/> and drives a local <see cref="Animator"/>
	/// to match it. Reusable for doors, chest lids, gates, levers, drawbridges — anything
	/// that toggles between two animated poses and must look the same on every peer.
	///
	/// This component is deliberately NOT an <see cref="IInteractable"/>: it only owns the
	/// synced state + animation. Drive it from whatever triggers the interaction —
	/// wire a <see cref="BaseInteractable.OnInteracted"/> UnityEvent to <see cref="RequestToggle"/>,
	/// call it from a hold-interactable, or have a phase controller call <see cref="ForceSetState"/>.
	///
	/// Sync model (per CLAUDE.md): a single <c>[Networked]</c> bool driven via state-authority
	/// RPC, with <c>OnChangedRender</c> applying it to the Animator. Drives an Animator *bool*
	/// parameter (not a trigger) so any peer — including late joiners — settles to the correct
	/// end pose regardless of missed ticks. Mirrors the <c>Radio</c> on/off pattern.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	public sealed class AnimatedInteractable : NetworkBehaviour
	{
		[Header("Animation")]
		[Tooltip("Animator driven by State. Auto-found in children on Spawned if left null.")]
		[SerializeField] private Animator _animator;

		[Tooltip("Animator bool parameter set from State. Author a bool param with Closed<->Open transitions on it.")]
		[SerializeField] private string _boolParam = "IsOpen";

		[Tooltip("If true, a freshly spawned peer (late joiner) snaps straight to the current pose instead of playing the transition.")]
		[SerializeField] private bool _snapOnSpawn = true;

		[Tooltip("Animator state name for the open pose — used only for the late-joiner snap.")]
		[SerializeField] private string _openStateName = "Open";

		[Tooltip("Animator state name for the closed pose — used only for the late-joiner snap.")]
		[SerializeField] private string _closedStateName = "Closed";

		[Header("Authority")]
		[Tooltip("Host re-validates that the requesting player is within this range before applying a player-driven toggle. 0 = skip the check (e.g. purely scripted props).")]
		[Min(0f)] [SerializeField] private float _hostValidationRange = 2.5f;

		/// <summary>True = open, false = closed. Only the state authority writes; all peers render via OnChangedRender.</summary>
		[Networked, OnChangedRender(nameof(OnStateChanged))]
		public NetworkBool State { get; private set; }

		private int _boolParamHash;

		private void Reset()
		{
			_animator = GetComponentInChildren<Animator>();
		}

		public override void Spawned()
		{
			if (_animator == null) _animator = GetComponentInChildren<Animator>();
			_boolParamHash = Animator.StringToHash(_boolParam);
			ApplyState(snap: _snapOnSpawn);
		}

		// =====================================================================
		// Drive points — call from any interaction trigger
		// =====================================================================

		/// <summary>Player-driven flip. Sends a state-authority RPC; the host re-validates range from the calling player.</summary>
		public void RequestToggle() => RPC_RequestSet(!State);

		/// <summary>Player-driven set to an explicit state. Host re-validates range before applying.</summary>
		public void RequestSetState(bool open) => RPC_RequestSet(open);

		/// <summary>
		/// Authority-only, non-player driver (e.g. the match-phase controller forcing doors shut at night).
		/// No range check — only call where you already hold state authority. No-op on non-authority peers.
		/// </summary>
		public void ForceSetState(bool open)
		{
			if (HasStateAuthority == false) return;
			State = open;
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_RequestSet(NetworkBool open, RpcInfo info = default)
		{
			if (Runner == null) return;

			if (_hostValidationRange > 0f)
			{
				var src = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
				var playerObj = Runner.GetPlayerObject(src);
				if (playerObj == null) return;

				float allowed = _hostValidationRange * 1.25f;
				if ((transform.position - playerObj.transform.position).sqrMagnitude > allowed * allowed) return;
			}

			State = open;
		}

		// =====================================================================
		// Render
		// =====================================================================

		private void OnStateChanged() => ApplyState(snap: false);

		private void ApplyState(bool snap)
		{
			if (_animator == null) return;

			_animator.SetBool(_boolParamHash, State);

			// Late joiners (and anyone snapping) jump straight to the end pose so they don't
			// replay a swing for a door that opened before they connected.
			if (snap)
			{
				string stateName = State ? _openStateName : _closedStateName;
				if (!string.IsNullOrEmpty(stateName))
					_animator.Play(stateName, 0, 1f);
			}
		}
	}
}
