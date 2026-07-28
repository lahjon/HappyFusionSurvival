using Fusion;
using Starter.Common.Interactions;
using UnityEngine;

namespace Starter.Run
{
	/// <summary>
	/// A working terminal on the city's own network. Using one grants network access and, with it, intel on one
	/// objective the team has not located yet.
	///
	/// <para>This is the run's difficulty dial, in the players' hands. A team can grind the city building by
	/// building, or spend the time to reach nav terminals and have the map hand them locations. Chaining them is
	/// deliberately front-loaded — the first is easy to reach, later ones sit deeper — so the shortcut has a
	/// rising cost rather than being strictly better than searching.</para>
	/// </summary>
	[RequireComponent(typeof(InteractionPrompt))]
	public sealed class NavigationTerminal : NetworkBehaviour, IInteractable
	{
		[Tooltip("Renderer tinted to show whether this terminal has been used.")]
		public Renderer ScreenRenderer;

		public Color DormantColor = new Color(0.2f, 0.16f, 0.1f);
		public Color UsedColor = new Color(0.35f, 0.7f, 1f);

		public AudioClip UseSound;

		/// <summary>Which navigation terminal this is. Host writes it at spawn.</summary>
		[Networked] public int NavIndex { get; private set; }

		public void HostConfigure(int navIndex, RunSite site)
		{
			if (!HasStateAuthority) return;
			NavIndex = navIndex;
		}

		public override void Spawned()
		{
			RunState.KnowledgeGained += OnKnowledgeGained;
			ApplyVisual();
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			RunState.KnowledgeGained -= OnKnowledgeGained;
		}

		private void OnKnowledgeGained(RunItem item) => ApplyVisual();

		public bool IsUsed =>
			RunState.Instance != null && RunState.Instance.HasKnowledge(new RunItem(RunItemKind.NavUnlock, NavIndex));

		private void ApplyVisual()
		{
			if (ScreenRenderer == null) return;

			var block = new MaterialPropertyBlock();
			ScreenRenderer.GetPropertyBlock(block);
			var color = IsUsed ? UsedColor : DormantColor;
			block.SetColor("_BaseColor", color);
			block.SetColor("_EmissionColor", IsUsed ? color : Color.black);
			ScreenRenderer.SetPropertyBlock(block);
		}

		// ── IInteractable ──────────────────────────────────────────────────────

		float IInteractable.InteractRange => 2.5f;
		bool IInteractable.CanInteract => !IsUsed;
		Vector3 IInteractable.InteractionPoint => transform.position;
		string IInteractable.LockedReason => IsUsed ? "Already queried" : null;
		string IInteractable.InteractLabel => IsUsed ? null : "Query the network";

		void IInteractable.OnInteract(InteractionScanner scanner)
		{
			if (IsUsed) return;

			var state = RunState.Instance;
			if (state == null) return;

			state.RPC_ReportKnowledge((byte)RunItemKind.NavUnlock, NavIndex);

			// Hand over the first objective the team still has no lead on. Querying a second terminal after
			// learning everything is not punished, it just tells you nothing new.
			int ordinal = FirstUnknownObjective(state);
			if (ordinal >= 0)
				state.RPC_ReportKnowledge((byte)RunItemKind.Intel, ordinal);

			if (UseSound != null)
				Starter.Shooter.AudioManager.Instance?.PlaySFX(UseSound, transform.position);
		}

		private static int FirstUnknownObjective(RunState state)
		{
			var plan = RunDirector.Instance != null ? RunDirector.Instance.Plan : null;
			if (plan == null) return -1;

			for (int i = 0; i < plan.ObjectiveSites.Count; i++)
				if (!state.HasKnowledge(new RunItem(RunItemKind.Intel, i))) return i;
			return -1;
		}
	}
}
