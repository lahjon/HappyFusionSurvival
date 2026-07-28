using Fusion;
using Starter.Common.Interactions;
using UnityEngine;

namespace Starter.Run
{
	/// <summary>
	/// Something written down: a sticky note on a monitor, a whiteboard, a shift roster, a scrawl on a wall.
	/// Reading one grants the team whatever it records — a door code, or the location of a terminal.
	///
	/// <para>Only the stash index is networked. The text and the knowledge it carries are read from the locally
	/// generated plan, which every peer already has. Replicating a few hundred strings would be the single
	/// largest thing on the wire in a run that otherwise sends almost nothing.</para>
	///
	/// <para>Knowledge is granted to the whole team, not the reader. Four players re-walking to the same note to
	/// each read the same four digits is busywork, and one of them would just say the number out loud anyway.</para>
	/// </summary>
	[RequireComponent(typeof(InteractionPrompt))]
	public sealed class ClueNote : NetworkBehaviour, IInteractable
	{
		[Tooltip("Seconds the note stays on screen after reading.")]
		[Min(1f)] public float DisplaySeconds = 6f;

		public AudioClip ReadSound;

		/// <summary>Index into the run plan's stash list. Host writes it at spawn.</summary>
		[Networked] public int StashIndex { get; private set; }

		/// <summary>Raised locally when this peer's player reads a note, for the HUD to display.</summary>
		public static event System.Action<string, float> NoteRead;

		public void HostConfigure(StashSpec stash)
		{
			if (!HasStateAuthority) return;
			StashIndex = stash.Index;
		}

		private StashSpec Spec
		{
			get
			{
				var plan = RunDirector.Instance != null ? RunDirector.Instance.Plan : null;
				if (plan == null || StashIndex < 0 || StashIndex >= plan.Stashes.Count) return null;
				return plan.Stashes[StashIndex];
			}
		}

		/// <summary>True once the team already knows what this note says — reading it again is pointless.</summary>
		public bool AlreadyKnown
		{
			get
			{
				var spec = Spec;
				if (spec == null || spec.Contents.IsNone) return false;
				return RunState.Instance != null && RunState.Instance.HasKnowledge(spec.Contents);
			}
		}

		// ── IInteractable ──────────────────────────────────────────────────────

		float IInteractable.InteractRange => 2f;
		bool IInteractable.CanInteract => true;
		Vector3 IInteractable.InteractionPoint => transform.position;
		string IInteractable.LockedReason => null;
		string IInteractable.InteractLabel => AlreadyKnown ? "Read again" : "Read";

		void IInteractable.OnInteract(InteractionScanner scanner)
		{
			var spec = Spec;
			string text = spec != null && !string.IsNullOrEmpty(spec.Text)
				? spec.Text
				: DescribeContents(spec);

			NoteRead?.Invoke(text, DisplaySeconds);

			if (ReadSound != null)
				Starter.Shooter.AudioManager.Instance?.PlaySFX(ReadSound, transform.position);

			if (spec == null || spec.Contents.IsNone || !spec.Contents.IsKnowledge) return;
			RunState.Instance?.RPC_ReportKnowledge((byte)spec.Contents.Kind, spec.Contents.Variant);
		}

		/// <summary>Fallback text for a note the plan gave no wording. Codes read as the actual digits, because a
		/// note that says "the code is somewhere" is not a clue.</summary>
		private string DescribeContents(StashSpec spec)
		{
			if (spec == null) return "The writing has faded.";

			if (spec.Contents.Kind == RunItemKind.PinCode)
			{
				var plan = RunDirector.Instance?.Plan;
				var barrier = FindBarrierForCode(plan, spec.Contents.Variant);
				if (barrier != null)
					return $"{barrier.Label}: {barrier.Code}";
			}

			return spec.Contents.IsNone
				? "Nothing useful."
				: $"Notes about {spec.Contents.Describe()}.";
		}

		private static BarrierSpec FindBarrierForCode(RunPlanData plan, int variant)
		{
			if (plan == null) return null;
			for (int i = 0; i < plan.Barriers.Count; i++)
			{
				var b = plan.Barriers[i];
				if (b.Requirement.Kind == RunItemKind.PinCode && b.Requirement.Variant == variant) return b;
			}
			return null;
		}
	}
}
