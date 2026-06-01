using System;
using UnityEngine;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// Per-item visual description read by the shared generic prefabs (Pickup_Generic, Hand_Generic)
	/// at spawn, so an ordinary item needs no bespoke prefab — just this data on its
	/// <see cref="ItemDefinition"/>. The world pickup builds its Visual child from
	/// <see cref="Mesh"/>/<see cref="Material"/>/<see cref="WorldScale"/>; the hand rig builds its
	/// held mesh from the same mesh/material at <see cref="HeldScale"/>/<see cref="HeldLocalPosition"/>.
	///
	/// For models that aren't a single mesh+material (e.g. a furniture prop), set
	/// <see cref="VisualOverride"/> — the generic prefab instantiates it as the visual instead.
	/// Items needing a fully bespoke functional prefab keep <c>ItemDefinition.WorldPrefab</c>/
	/// <c>HandPrefab</c> set (those take priority over the generic path entirely).
	/// </summary>
	[Serializable]
	public sealed class ItemVisual
	{
		[Tooltip("Mesh shown in the world pickup and in-hand. Ignored when VisualOverride is set.")]
		public Mesh Mesh;

		[Tooltip("Material for the mesh. Ignored when VisualOverride is set.")]
		public Material Material;

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
		[Tooltip("Local scale of the held mesh on the hand rig.")]
		public Vector3 HeldScale = Vector3.one;

		[Tooltip("Local position of the held mesh relative to the hand rig root.")]
		public Vector3 HeldLocalPosition;

		[Tooltip("Local euler rotation of the held mesh relative to the hand rig root.")]
		public Vector3 HeldLocalEuler;

		[Header("Override")]
		[Tooltip("Optional visual-only prefab instantiated instead of building a primitive from Mesh/Material " +
		         "(e.g. a furniture model). Has no gameplay components.")]
		public GameObject VisualOverride;
	}
}
