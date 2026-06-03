using System;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Gadget facet for the grapple hook: LMB fires a hook at the surface the player is looking at and reels
	/// the player straight to it (charge-gated, no damage). The hook is a movement ability, so unlike the
	/// passive radar it cannot live purely on the local <see cref="HeldGadget"/> — the reel drives networked
	/// KCC movement. This capability holds only authoring data; the networked state (anchor, is-grappling,
	/// charges, cooldown) lives on <see cref="Player"/>, and <see cref="Player.ProcessFireInput"/> dispatches
	/// LMB into the reel when this gadget is held. The runtime <see cref="HeldGrapple"/> attached here is just
	/// the local rope visual.
	///
	/// Charges are the device's whole life: each fire spends one (ridden on the slot's Loaded), and at zero the
	/// grapple is useless — there is no recharge.
	/// </summary>
	[Serializable]
	public sealed class GrappleGadgetCapability : GadgetCapability
	{
		[Header("Reel")]
		[Tooltip("Maximum distance (m) the hook can reach. Pressing fire with nothing solid in range is a no-op (no charge spent).")]
		public float Range = 25f;
		[Tooltip("Speed (m/s) the player is pulled toward the anchor while reeling.")]
		public float ReelSpeed = 22f;
		[Tooltip("Stop reeling and detach once the player is within this distance (m) of the anchor.")]
		public float ArrivalDistance = 1.5f;
		[Tooltip("Minimum seconds between fires — also paces the detach -> refire gap.")]
		public float FireCooldown = 0.35f;
		[Tooltip("Hard safety cap (seconds) on a single reel. If the player hasn't arrived by now — e.g. wedged " +
			"against an obstacle between them and the anchor — the grapple auto-detaches so they can't get stuck. " +
			"The stall check usually releases sooner.")]
		public float MaxReelTime = 3f;

		[Header("Charges")]
		[Tooltip("Number of grapples this device carries. Each successful fire spends one; at zero the gadget is useless (no recharge).")]
		[Min(0)]
		public int Charges = 3;

		[Header("Rope visual")]
		[Tooltip("Color of the rope drawn from the hand to the anchor while reeling.")]
		public Color RopeColor = new(0.85f, 0.7f, 0.4f, 1f);
		[Tooltip("World-space width (m) of the rope line.")]
		public float RopeWidth = 0.03f;

		public override bool UsesPrimary => true;
		public override int MaxCharges => Charges;

		public override HeldGadget CreateRuntime(GameObject host)
		{
			var rope = host.AddComponent<HeldGrapple>();
			rope.Initialize(this);
			return rope;
		}
	}
}
