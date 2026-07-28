using Fusion;
using Starter.Common.Interactions;
using UnityEngine;

namespace Starter.Run
{
	/// <summary>
	/// A district's electrical intake. Throwing it restores power to everything in that district — mag-locks
	/// release, lifts run, card readers wake up.
	///
	/// <para>Power is the one requirement that is not carried and not knowledge in the ordinary sense: it is a
	/// world state one player can switch on for everyone, permanently. The planner always puts breakers in
	/// ungated buildings, because a power gate whose breaker is behind the power gate is the classic randomizer
	/// deadlock.</para>
	///
	/// <para>Restoring power is not free. Lights come back on across the district, which is exactly the thing
	/// players spend the rest of the run avoiding.</para>
	/// </summary>
	[RequireComponent(typeof(InteractionPrompt))]
	public sealed class PowerBreaker : NetworkBehaviour, IInteractable
	{
		[Tooltip("Lever or handle rotated when the breaker is thrown.")]
		public Transform Lever;
		public Vector3 ThrownRotation = new Vector3(0f, 0f, -60f);

		public AudioClip ThrowSound;

		/// <summary>Index into the run plan's stash list. Host writes it at spawn.</summary>
		[Networked] public int StashIndex { get; private set; }

		private Quaternion _restRotation;

		public void HostConfigure(StashSpec stash)
		{
			if (!HasStateAuthority) return;
			StashIndex = stash.Index;
		}

		public override void Spawned()
		{
			if (Lever != null) _restRotation = Lever.localRotation;
			RunState.KnowledgeGained += OnKnowledgeGained;
			ApplyVisual();
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			RunState.KnowledgeGained -= OnKnowledgeGained;
		}

		private void OnKnowledgeGained(RunItem item) => ApplyVisual();

		private StashSpec Spec
		{
			get
			{
				var plan = RunDirector.Instance != null ? RunDirector.Instance.Plan : null;
				if (plan == null || StashIndex < 0 || StashIndex >= plan.Stashes.Count) return null;
				return plan.Stashes[StashIndex];
			}
		}

		public bool IsThrown
		{
			get
			{
				var spec = Spec;
				return spec != null && RunState.Instance != null && RunState.Instance.HasKnowledge(spec.Contents);
			}
		}

		private void ApplyVisual()
		{
			if (Lever == null) return;
			Lever.localRotation = IsThrown ? _restRotation * Quaternion.Euler(ThrownRotation) : _restRotation;
		}

		// ── IInteractable ──────────────────────────────────────────────────────

		float IInteractable.InteractRange => 2f;
		bool IInteractable.CanInteract => !IsThrown;
		Vector3 IInteractable.InteractionPoint => transform.position;
		string IInteractable.LockedReason => IsThrown ? "Already live" : null;
		string IInteractable.InteractLabel => IsThrown ? null : "Restore power";

		void IInteractable.OnInteract(InteractionScanner scanner)
		{
			var spec = Spec;
			if (spec == null || spec.Contents.IsNone || IsThrown) return;

			RunState.Instance?.RPC_ReportKnowledge((byte)spec.Contents.Kind, spec.Contents.Variant);

			if (ThrowSound != null)
				Starter.Shooter.AudioManager.Instance?.PlaySFX(ThrowSound, transform.position);
		}
	}
}
