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
	}
}
