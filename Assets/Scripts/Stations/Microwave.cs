namespace Starter.Shooter
{
	/// <summary>
	/// Networked microwave — a <see cref="CookingAppliance"/> driven by two physical button children
	/// (each a sibling <c>BaseInteractable</c> wired via its <c>OnInteracted</c> UnityEvent to a method here):
	///
	/// - <b>Door button</b> → <see cref="RequestToggleDoor"/>: opens / closes the door. Opening it mid-run
	///   aborts the cook (no finish ding), the base's safety cut-off.
	/// - <b>Start button</b> → <see cref="RequestStart"/>: runs a cycle; ignored while the door is open or
	///   already running (pressing while running stops it).
	///
	/// All the actual behaviour — networked door + swing, the cook timer, hum, and finish ding — lives in
	/// <see cref="CookingAppliance"/> / <see cref="OpenableAppliance"/>. This leaf only re-exposes the base's
	/// generic <see cref="OpenableAppliance.RequestToggleOpen"/> / <see cref="CookingAppliance.RequestToggleCook"/>
	/// (and <see cref="CookingAppliance.IsCooking"/>) under the microwave's original names so the existing
	/// <see cref="MicrowaveButton"/> and its prefab UnityEvent bindings keep working unchanged.
	///
	/// The body itself is not interactable (the base's <c>Door Is Interactable</c> is off) — the two
	/// buttons are the only way in, and there's no interior volume, so nothing is gated.
	/// </summary>
	public sealed class Microwave : CookingAppliance
	{
		/// <summary>Back-compat: the microwave's "running" light/label state. Same as <see cref="CookingAppliance.IsCooking"/>.</summary>
		public bool IsRunning => IsCooking;

		/// <summary>Door button drive point. Forwards to the base door toggle.</summary>
		public void RequestToggleDoor() => RequestToggleOpen();

		/// <summary>Start/Stop button drive point. Forwards to the base cook toggle.</summary>
		public void RequestStart() => RequestToggleCook();
	}
}
