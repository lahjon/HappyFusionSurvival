namespace Starter.Shooter
{
	/// <summary>
	/// A plain openable container — fridge, pantry, chest, cabinet, locker. All behaviour lives in
	/// <see cref="OpenableAppliance"/>: the door is the interactable, interacting toggles it, the
	/// interior light is on while open, and the contents (placed items / an <see cref="ItemSpawner"/>'s
	/// anchors inside the interior volume) can't be interacted with until it's opened — and persist
	/// exactly across open/close.
	///
	/// This leaf exists so containers and cooking appliances (<see cref="CookingAppliance"/>) are
	/// distinct components to wire in the Inspector, sharing one base. Add cooking by using
	/// <see cref="Oven"/> instead.
	/// </summary>
	public sealed class ContainerOpenable : OpenableAppliance
	{
	}
}
