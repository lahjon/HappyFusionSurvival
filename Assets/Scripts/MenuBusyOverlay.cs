using TMPro;
using UnityEngine;

namespace Starter.MainMenu
{
	/// <summary>
	/// Shows a full-screen, raycast-absorbing overlay while a networked transition is in flight (Host/Join connecting,
	/// or the host loading the game scene), locking out all menu interaction so the player can't fire a second action
	/// mid-load. The underlying re-entrancy guards (<see cref="NetworkLauncher.IsBusy"/>, <see cref="Shooter.LobbyManager.IsStarting"/>)
	/// already make repeat clicks harmless — this is the visible lock + feedback on top of that.
	///
	/// Lives on an always-active object (so Update runs even while the overlay is hidden) and toggles a separate
	/// overlay child. The overlay must be the LAST sibling under the Canvas so it renders on top and catches all clicks.
	/// </summary>
	public sealed class MenuBusyOverlay : MonoBehaviour
	{
		[Tooltip("Full-screen overlay GameObject (an Image with Raycast Target on). Toggled active while busy.")]
		public GameObject Overlay;
		[Tooltip("Optional label on the overlay; shows the launcher status (Hosting… / Joining…) or 'Loading…'.")]
		public TMP_Text Label;

		private void Update()
		{
			bool busy = NetworkLauncher.IsConnecting
				|| (Shooter.LobbyManager.Instance != null && Shooter.LobbyManager.Instance.IsStarting);

			if (Overlay != null && Overlay.activeSelf != busy)
				Overlay.SetActive(busy);

			if (busy && Label != null)
			{
				var status = NetworkLauncher.Instance != null ? NetworkLauncher.Instance.Status : null;
				Label.text = string.IsNullOrEmpty(status) ? "Loading…" : status;
			}
		}
	}
}
