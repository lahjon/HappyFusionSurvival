using Fusion;
using Starter.Common.Interactions;
using UnityEngine;

namespace Starter.Run
{
	/// <summary>
	/// One of the four terminals the team has to bring online. Finding it is most of the game; using it is one
	/// button and a loud, irreversible consequence — every monster in the district starts hunting.
	///
	/// <para>Only the ordinal is networked. Everything else — where it is, what it is called, how many floors up
	/// — is read from the locally generated plan via <see cref="RunDirector"/>, because every peer has an
	/// identical copy of it.</para>
	/// </summary>
	[RequireComponent(typeof(InteractionPrompt))]
	public sealed class ObjectiveTerminal : NetworkBehaviour, IInteractable
	{
		[Tooltip("Renderer tinted to show whether this terminal is live.")]
		public Renderer ScreenRenderer;

		public Color DormantColor = new Color(0.15f, 0.18f, 0.22f);
		public Color ActiveColor = new Color(0.2f, 1f, 0.55f);

		[Tooltip("Seconds the activation takes. The team is stuck here and exposed for the duration.")]
		[Min(0f)] public float ActivationSeconds = 4f;

		public AudioClip ActivationSound;

		/// <summary>Which of the four this is (0..3). Host writes it at spawn.</summary>
		[Networked] public int Ordinal { get; private set; }

		private bool _lastActive;

		public void HostConfigure(int ordinal, RunSite site)
		{
			if (!HasStateAuthority) return;
			Ordinal = ordinal;
		}

		public override void Spawned()
		{
			RunState.ObjectiveActivated += OnObjectiveActivated;
			ApplyVisual(IsActive);
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			RunState.ObjectiveActivated -= OnObjectiveActivated;
		}

		private void OnObjectiveActivated(int ordinal)
		{
			if (ordinal != Ordinal) return;
			ApplyVisual(true);
		}

		public bool IsActive => RunState.Instance != null && RunState.Instance.IsObjectiveActive(Ordinal);

		private void ApplyVisual(bool active)
		{
			if (active && !_lastActive && ActivationSound != null)
				Starter.Shooter.AudioManager.Instance?.PlaySFX(ActivationSound, transform.position);

			_lastActive = active;
			if (ScreenRenderer == null) return;

			var block = new MaterialPropertyBlock();
			ScreenRenderer.GetPropertyBlock(block);
			block.SetColor("_BaseColor", active ? ActiveColor : DormantColor);
			block.SetColor("_EmissionColor", active ? ActiveColor : Color.black);
			ScreenRenderer.SetPropertyBlock(block);
		}

		// ── IInteractable ──────────────────────────────────────────────────────

		float IInteractable.InteractRange => 2.5f;
		bool IInteractable.CanInteract => !IsActive;
		Vector3 IInteractable.InteractionPoint => transform.position;
		string IInteractable.LockedReason => IsActive ? "Already online" : null;

		string IInteractable.InteractLabel =>
			IsActive ? null : $"Activate terminal {(char)('A' + Ordinal)}";

		void IInteractable.OnInteract(InteractionScanner scanner)
		{
			if (IsActive) return;
			RunState.Instance?.RPC_RequestActivateObjective(Ordinal, scanner.transform.position);
		}
	}
}
