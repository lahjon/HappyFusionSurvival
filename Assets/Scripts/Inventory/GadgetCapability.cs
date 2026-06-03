using System;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Item facet that turns an item into a handheld gadget device. At equip, <c>Inventory.RefreshHeldItem</c>
	/// looks up the first <see cref="GadgetCapability"/> on the held item and calls <see cref="CreateRuntime"/>,
	/// which attaches the matching <see cref="HeldGadget"/> behavior to the (generic) hand instance and seeds it
	/// from this authoring data. Because the gadget rides on <c>Hand_Generic</c>, a gadget item needs no bespoke
	/// HandPrefab — and it composes freely with other facets (a gadget that is also a weapon is just an item
	/// carrying both capabilities).
	///
	/// Abstract on purpose: one concrete subclass per device (e.g. <see cref="RadarGadgetCapability"/>), each
	/// holding that device's tuning and knowing which <see cref="HeldGadget"/> to spawn. Mirrors the
	/// <c>ConsumableCapability.Apply</c> contract — the capability is stateless authoring data plus a factory
	/// hook; per-instance runtime state lives on the spawned <see cref="HeldGadget"/>, never here.
	/// </summary>
	[Serializable]
	public abstract class GadgetCapability : ItemCapability
	{
		/// <summary>
		/// Attach this gadget's runtime behavior to <paramref name="host"/> (the held hand instance) and
		/// initialize it from this data. Returns the spawned gadget. Called once per equip.
		/// </summary>
		public abstract HeldGadget CreateRuntime(GameObject host);
	}
}
