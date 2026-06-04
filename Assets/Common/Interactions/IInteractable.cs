using UnityEngine;

namespace Starter.Common.Interactions
{
	/// <summary>
	/// Implemented by any component that wants to participate in the local player's
	/// <see cref="InteractionScanner"/>. The scanner finds the best in-range, in-view
	/// candidate each frame and invokes <see cref="OnInteract"/> on it when the player
	/// presses the Interact key. The interaction system makes no assumptions about
	/// what the target does — chests open UIs, pickups grant items, doors open, etc.
	/// </summary>
	public interface IInteractable
	{
		/// <summary>Maximum distance from the player at which this can be interacted with.</summary>
		float InteractRange { get; }

		/// <summary>True if this is currently interactable (e.g. not locked, not destroyed).</summary>
		bool CanInteract { get; }

		/// <summary>
		/// World position used by the scanner for line-of-sight and range checks.
		/// Default implementations should return <c>transform.position</c>.
		/// </summary>
		Vector3 InteractionPoint { get; }

		/// <summary>
		/// Optional short label surfaced when the local player can't interact right now
		/// (e.g. "In use", "Locked"). Returned as a toast by the scanner.
		/// </summary>
		string LockedReason { get; }

		/// <summary>Invoked when the player presses Interact and this is the chosen target.</summary>
		void OnInteract(InteractionScanner scanner);

		/// <summary>Short action label shown in the screen-space HUD when this is the current target (e.g. "Open chest", "Enter vehicle"). Defaults to a generic prompt.</summary>
		string InteractLabel => "Press E to interact";
	}

	/// <summary>
	/// Optional facet letting an interactable place its on-screen prompt indicator at a
	/// transform other than its own. The <see cref="InteractionScanner"/> anchors the shared
	/// prompt HUD to <see cref="PromptAnchor"/> (still offset by any <see cref="InteractionPrompt.LocalOffset"/>)
	/// when this returns a non-null transform; otherwise it falls back to the target's own transform.
	/// Useful when the indicator should sit on a child marker (e.g. a door handle, a lever tip)
	/// rather than the interactable's pivot.
	/// </summary>
	public interface IInteractionPromptAnchor
	{
		/// <summary>Transform the prompt indicator should follow, or null to use the interactable's own transform.</summary>
		Transform PromptAnchor { get; }
	}

	/// <summary>
	/// Optional facet for interactables that come in clusters where only the nearest one
	/// should ever be selectable (e.g. the seats of a single vehicle). Candidates sharing
	/// the same non-null <see cref="InteractionGroupKey"/> are collapsed by the
	/// <see cref="InteractionScanner"/> to the single closest member before the usual
	/// look-alignment scoring runs — so you can't, say, target the passenger seat through
	/// the cab while standing at the driver's door.
	/// </summary>
	public interface IInteractableGroup : IInteractable
	{
		/// <summary>Shared identity for the cluster (return the parent vehicle, door rig, etc.). Null opts out of grouping.</summary>
		object InteractionGroupKey { get; }
	}
}
