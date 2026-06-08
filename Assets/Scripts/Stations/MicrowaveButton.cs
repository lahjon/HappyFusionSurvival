using Starter.Common.Interactions;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// One physical button on a <see cref="Microwave"/>. A local <see cref="IInteractable"/> — the
	/// microwave itself owns every bit of networked state, so the button only forwards a tap to it.
	/// Two of these live on the prefab (one <see cref="ButtonKind.ToggleDoor"/>, one
	/// <see cref="ButtonKind.Start"/>) so the <see cref="InteractionScanner"/> can target each
	/// independently by look-alignment — that's the "two buttons" the player aims at.
	///
	/// Local-only by design (plain MonoBehaviour): pressing it just calls the owning microwave's
	/// public request method, which sends the state-authority RPC. Add an
	/// <see cref="InteractionPrompt"/> sibling for the on-screen prompt.
	/// </summary>
	[RequireComponent(typeof(Collider))]
	[RequireComponent(typeof(InteractionPrompt))]
	public sealed class MicrowaveButton : MonoBehaviour, IInteractable
	{
		public enum ButtonKind { ToggleDoor, Start }

		[Tooltip("Owning microwave. Auto-found in parents on Reset.")]
		[SerializeField] private Microwave _microwave;

		[Tooltip("Which microwave action this button triggers.")]
		[SerializeField] private ButtonKind _kind = ButtonKind.ToggleDoor;

		[Min(0f)] [SerializeField] private float _interactRange = 2.5f;

		[Tooltip("Prompt label shown when aiming at this button. For the Start button this is the idle/'Start' label.")]
		[SerializeField] private string _label = "Press";

		[Tooltip("Start button only: prompt label shown while the microwave is running (acts as the Stop label).")]
		[SerializeField] private string _runningLabel = "Stop";

		[Tooltip("Optional local click played on press.")]
		[SerializeField] private AudioClip _clickClip;
		[Range(0f, 1f)] [SerializeField] private float _clickVolume = 1f;

		private void Reset() => _microwave = GetComponentInParent<Microwave>();

		// --- IInteractable ---

		float IInteractable.InteractRange => _interactRange;
		bool IInteractable.CanInteract => _microwave != null && isActiveAndEnabled;
		Vector3 IInteractable.InteractionPoint => transform.position;
		string IInteractable.LockedReason => string.Empty;
		// Start button flips its label between "Start" and "Stop" as the microwave runs;
		// read live each frame by the InteractionScanner/prompt (the Door.cs pattern).
		string IInteractable.InteractLabel =>
			_kind == ButtonKind.Start && _microwave != null && _microwave.IsRunning
				? _runningLabel
				: _label;

		void IInteractable.OnInteract(InteractionScanner scanner)
		{
			if (_microwave == null) return;

			if (_clickClip != null)
				AudioManager.Instance?.PlaySFX(_clickClip, transform.position, _clickVolume);

			switch (_kind)
			{
				case ButtonKind.ToggleDoor: _microwave.RequestToggleDoor(); break;
				case ButtonKind.Start:      _microwave.RequestStart();      break;
			}
		}
	}
}
