using UnityEngine;
using UnityEngine.Events;

namespace Starter.Common.Interactions
{
	/// <summary>
	/// Generic local-only <see cref="IInteractable"/> for scene props that just need to
	/// fire a UnityEvent when the player interacts. Drop this on any GameObject (or use
	/// the <c>BaseInteractable</c> prefab) and wire <see cref="OnInteracted"/> in the
	/// Inspector to drive any local response — open a UI, play VFX, invoke an RPC on a
	/// sibling NetworkBehaviour, etc.
	///
	/// For interactions that mutate networked state, wire the event to a method that
	/// calls an [Rpc] on a NetworkBehaviour sibling — this component itself stays local
	/// so it can be used on pure-visual props without forcing a NetworkObject.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class BaseInteractable : MonoBehaviour, IInteractable
	{
		[Header("Authoring")]
		[Tooltip("Max distance from the local player at which this can be interacted with.")]
		[Min(0f)] public float InteractRangeValue = 2f;

		[Tooltip("Optional toast shown when CanInteract is false (e.g. \"Locked\", \"In use\"). Leave empty for silent.")]
		public string LockedReasonText = string.Empty;

		[Tooltip("Optional offset (local space) for the point the scanner uses for range + line-of-sight checks. Leave zero to use transform.position.")]
		public Vector3 InteractionPointOffset = Vector3.zero;

		[Header("State")]
		[Tooltip("Toggle at runtime to enable/disable this interactable without removing the component.")]
		public bool Interactable = true;

		[Header("Events")]
		[Tooltip("Fires locally on whoever interacted. For multiplayer state changes, wire this to a method that invokes an RPC.")]
		public UnityEvent OnInteracted;

		/// <summary>The scanner that triggered the most recent OnInteracted (null before any interaction). Lets event handlers locate the local player without needing a parameter.</summary>
		public InteractionScanner LastScanner { get; private set; }

		// --- IInteractable ---

		float IInteractable.InteractRange => InteractRangeValue;
		bool IInteractable.CanInteract => Interactable && isActiveAndEnabled;
		Vector3 IInteractable.InteractionPoint => transform.TransformPoint(InteractionPointOffset);
		string IInteractable.LockedReason => LockedReasonText;

		void IInteractable.OnInteract(InteractionScanner scanner)
		{
			LastScanner = scanner;
			OnInteracted?.Invoke();
		}
	}
}
