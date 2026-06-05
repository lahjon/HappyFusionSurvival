namespace Starter.Shooter
{
	/// <summary>
	/// A networked oven: open the door to place / take food (gated interior contents + interior light,
	/// from <see cref="OpenableAppliance"/>) and run a timed heat cycle (from <see cref="CookingAppliance"/>).
	///
	/// Wiring: the door is the interactable (open/close). Put a control on a knob — a
	/// <c>BaseInteractable</c> whose <c>OnInteracted</c> UnityEvent calls <see cref="CookingAppliance.RequestToggleCook"/> —
	/// to start/stop heating. Point the base's interior volume at the cavity so food can't be grabbed until
	/// the door is open, and use the "active while cooking" list for the coil glow.
	///
	/// All behaviour is inherited; this leaf exists so an oven is a distinct component to wire and a place
	/// for any oven-only flourishes later.
	/// </summary>
	[UnityEngine.AddComponentMenu("Starter/Stations/Oven")]
	public sealed class Oven : CookingAppliance
	{
	}
}
