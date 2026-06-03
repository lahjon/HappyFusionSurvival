using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// A networked, host-authoritative physics object that a <see cref="WaterVolume"/> can push
	/// around with buoyancy. This is the single abstraction the water system talks to, so it never
	/// needs to know whether the thing in the water is a loose <see cref="PhysicsProp"/> or a dropped
	/// <see cref="Starter.Common.Inventory.PickupableItem"/> — both expose the same contract.
	///
	/// Mirrors how <see cref="IKnockbackable"/> already lets combat shove any rigidbody object without
	/// caring about its concrete type. Only the state authority simulates these rigidbodies (proxies
	/// are kinematic), so the WaterVolume just reads <see cref="Body"/> and skips kinematic proxies.
	/// </summary>
	public interface IBuoyantBody
	{
		/// <summary>The simulated Rigidbody. Null until <c>Spawned()</c>; kinematic on proxies.</summary>
		Rigidbody Body { get; }

		/// <summary>
		/// True = this object rises to the surface and bobs. False (default) = it sinks.
		/// Authored per-object so "everything sinks unless told otherwise" is the natural default.
		/// </summary>
		bool Floats { get; }
	}
}
