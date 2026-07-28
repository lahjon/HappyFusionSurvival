using Starter.Common.Interactions;
using UnityEngine;

namespace Starter.Run
{
	/// <summary>
	/// The runtime face of one <see cref="BarrierSpec"/>: the thing standing between the players and where they
	/// want to go.
	///
	/// <para><b>One component, every kind of block.</b> A locked door, a keypad, a card reader, a chained shutter,
	/// a mag-lock with no power and a welded-shut fire exit are all this component with a different
	/// <see cref="BarrierKind"/> and requirement. That is what makes the system reusable: the building generator
	/// places portals, the run planner decides which portals get blocked and how, and none of them need a special
	/// case. Adding a new kind of block means adding an enum value and a branch in
	/// <see cref="LocalCanOpen"/> — not a new door class, prefab or interaction path.</para>
	///
	/// <para>Deliberately a plain <c>MonoBehaviour</c>. Its state lives in <see cref="RunState"/>'s barrier array,
	/// so a barrier is free to create and destroy as the city streams in and out, and the run's progress survives
	/// a player walking away and coming back.</para>
	/// </summary>
	[RequireComponent(typeof(InteractionPrompt))]
	public sealed class Barrier : MonoBehaviour, IInteractable
	{
		/// <summary>Range at which a barrier can be operated. Also the basis for the host's re-validation, so it
		/// lives here as a constant rather than a per-instance field a client could disagree about.</summary>
		public const float InteractRangeMeters = 2.5f;

		[Tooltip("Geometry that physically blocks the way. Deactivated when the barrier opens.")]
		public GameObject BlockerRoot;

		[Tooltip("Optional pivot swung open when the barrier is defeated (a door leaf, a shutter).")]
		public Transform OpenPivot;

		[Tooltip("Local-space rotation applied to OpenPivot when open.")]
		public Vector3 OpenRotation = new Vector3(0f, 90f, 0f);

		[Tooltip("Degrees per second the pivot swings.")]
		[Min(1f)] public float OpenSpeed = 180f;

		[Tooltip("Played locally when this barrier is defeated.")]
		public AudioClip OpenSound;

		/// <summary>Index into the run plan's barrier list. -1 until configured.</summary>
		public int BarrierIndex { get; private set; } = -1;

		public BarrierKind Kind { get; private set; }

		private BarrierSpec _spec;
		private bool _open;
		private Quaternion _closedRotation;
		private Quaternion _openRotation;

		// =====================================================================
		// Setup
		// =====================================================================

		/// <summary>Bind this instance to a planned barrier. Called by the realiser as the city streams in; safe
		/// to call again if the object is reused.</summary>
		public void Configure(BarrierSpec spec)
		{
			_spec = spec;
			BarrierIndex = spec != null ? spec.Index : -1;
			Kind = spec != null ? spec.Kind : BarrierKind.KeyedLock;

			if (OpenPivot != null)
			{
				_closedRotation = OpenPivot.localRotation;
				_openRotation = _closedRotation * Quaternion.Euler(OpenRotation);
			}

			EnsureNavObstacle();
			RefreshFromState(true);
		}

		/// <summary>
		/// A closed barrier carves the baked NavMesh so monster paths honestly route around it; because the
		/// obstacle sits on <see cref="BlockerRoot"/>, opening the barrier (which deactivates the blocker)
		/// un-carves the hole and the doorway becomes a route again. Added at runtime so the prefab needs no
		/// hand-wired component.
		/// </summary>
		private void EnsureNavObstacle()
		{
			if (BlockerRoot == null || BlockerRoot.GetComponent<UnityEngine.AI.NavMeshObstacle>() != null) return;

			var obstacle = BlockerRoot.AddComponent<UnityEngine.AI.NavMeshObstacle>();
			obstacle.carving = true;
			obstacle.carveOnlyStationary = true;
			obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;

			var col = BlockerRoot.GetComponentInChildren<Collider>();
			if (col != null)
			{
				Vector3 size = BlockerRoot.transform.InverseTransformVector(col.bounds.size);
				obstacle.center = BlockerRoot.transform.InverseTransformPoint(col.bounds.center);
				obstacle.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
			}
		}

		private void OnEnable()
		{
			RunState.BarriersChanged += OnBarriersChanged;
			RefreshFromState(true);
		}

		private void OnDisable()
		{
			RunState.BarriersChanged -= OnBarriersChanged;
		}

		private void OnBarriersChanged() => RefreshFromState(false);

		/// <summary>Pull the authoritative open/closed state and apply it. <paramref name="snap"/> skips the
		/// animation (used when the barrier is first built, so a door the team opened an hour ago is already
		/// standing open when they come back).</summary>
		private void RefreshFromState(bool snap)
		{
			var state = RunState.Instance;
			bool nowOpen = state != null && BarrierIndex >= 0 && state.IsBarrierOpen(BarrierIndex);
			if (nowOpen == _open && !snap) return;

			bool justOpened = nowOpen && !_open;
			_open = nowOpen;

			if (BlockerRoot != null && (snap || !HasPivot))
				BlockerRoot.SetActive(!_open);

			if (snap && OpenPivot != null)
				OpenPivot.localRotation = _open ? _openRotation : _closedRotation;

			if (justOpened && !snap && OpenSound != null)
				Starter.Shooter.AudioManager.Instance?.PlaySFX(OpenSound, transform.position);
		}

		private bool HasPivot => OpenPivot != null;

		private void Update()
		{
			if (OpenPivot == null) return;

			var target = _open ? _openRotation : _closedRotation;
			if (OpenPivot.localRotation == target)
			{
				// Once the leaf has finished swinging, drop the collider so players can walk through.
				if (BlockerRoot != null && BlockerRoot.activeSelf == _open)
					BlockerRoot.SetActive(!_open);
				return;
			}

			OpenPivot.localRotation = Quaternion.RotateTowards(
				OpenPivot.localRotation, target, OpenSpeed * Time.deltaTime);
		}

		// =====================================================================
		// IInteractable
		// =====================================================================

		float IInteractable.InteractRange => InteractRangeMeters;

		// CanInteract is false whenever the player cannot actually defeat the barrier, so the scanner surfaces
		// LockedReason as a toast. A refusal that names what is missing is itself a clue — it is how players
		// learn what a run wants from them — so the reason string is written to be read, not to be a stub.
		bool IInteractable.CanInteract => !_open && _spec != null && LocalCanOpen(out _);

		Vector3 IInteractable.InteractionPoint => transform.position;

		string IInteractable.LockedReason
		{
			get
			{
				if (_open || _spec == null) return null;
				LocalCanOpen(out string reason);
				return reason;
			}
		}

		string IInteractable.InteractLabel =>
			_open || _spec == null ? null : $"Open {_spec.Label ?? Kind.Label()}";

		void IInteractable.OnInteract(InteractionScanner scanner)
		{
			if (_open || _spec == null) return;
			if (!LocalCanOpen(out _)) return;

			RunState.Instance?.RPC_RequestOpenBarrier(BarrierIndex, scanner.transform.position);
		}

		// =====================================================================
		// Requirement check (local, advisory — the host re-checks)
		// =====================================================================

		/// <summary>
		/// Whether the local player can defeat this barrier right now, and why not if they cannot. Purely a
		/// prediction for the prompt and the toast; <see cref="RunDirector.HostCanOpen"/> is the authority.
		/// </summary>
		public bool LocalCanOpen(out string reason)
		{
			reason = null;
			if (_spec == null) return false;

			if (Kind.IsHardBlock())
			{
				reason = "It is welded shut. There has to be another way in.";
				return false;
			}

			var director = RunDirector.Instance;
			if (director == null) return false;

			if (director.LocalPlayerSatisfies(_spec)) return true;

			reason = $"{_spec.Label ?? Kind.Label()} — you need {_spec.Requirement.Describe()}.";
			return false;
		}
	}
}
