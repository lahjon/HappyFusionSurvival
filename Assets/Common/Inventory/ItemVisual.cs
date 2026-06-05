using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// Per-item visual description read by the shared generic prefabs (Pickup_Generic, Hand_Generic)
	/// at spawn, so an ordinary item needs no bespoke prefab — just this data on its
	/// <see cref="ItemData"/>. Both the world pickup and the hand rig instantiate
	/// <see cref="Prefab"/> as their visual child and position it via the scale/offset fields below
	/// (<see cref="WorldScale"/> in the world, <see cref="HeldScale"/>/<see cref="HeldLocalPosition"/>/
	/// <see cref="HeldLocalEuler"/> in the hand). <see cref="Prefab"/> is visual-only — no gameplay
	/// components.
	///
	/// If <see cref="Prefab"/> is null the generic rig falls back to its own placeholder primitive
	/// (a plain cube). Items needing real functional components in a context set
	/// <c>ItemData.WorldPrefab</c>/<c>HandPrefab</c> instead — those replace the generic prefab
	/// entirely for that context.
	/// </summary>
	[Serializable]
	public sealed class ItemVisual
	{
		[Tooltip("Visual-only prefab instantiated as this item's model on the shared world pickup and " +
		         "hand rig. The single source of an ordinary item's look — no gameplay components. " +
		         "Leave null to fall back to the generic rig's placeholder primitive.")]
		[FormerlySerializedAs("VisualOverride")]
		public GameObject Prefab;

		[Header("World pickup")]
		[Tooltip("Local scale of the Visual child on the world pickup.")]
		public Vector3 WorldScale = Vector3.one * 0.3f;

		[Tooltip("Slow bob up/down on the world pickup (ItemView flair).")]
		public bool WorldBob;

		[Tooltip("Slow spin on the world pickup (ItemView flair).")]
		public bool WorldRotate;

		[Tooltip("Local offset of the interaction prompt above the world pickup.")]
		public Vector3 PromptOffset;

		[Header("In-hand")]
		[Tooltip("Local scale of the held model on the hand rig.")]
		public Vector3 HeldScale = Vector3.one;

		[Tooltip("Local position of the held model relative to the hand rig root.")]
		public Vector3 HeldLocalPosition;

		[Tooltip("Local euler rotation of the held model relative to the hand rig root.")]
		public Vector3 HeldLocalEuler;
	}
}
