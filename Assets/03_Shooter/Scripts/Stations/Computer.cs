using Starter.Common.Interactions;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Networked desktop computer station. On interact, hands off to the local
	/// <see cref="ComputerSession"/> which switches to the inventory input context
	/// and lerps the camera toward <see cref="ScreenViewTransform"/>.
	///
	/// No networked state in v1 — "sitting at the PC" is purely local UX, the same way
	/// <see cref="CraftingBench"/> handles opening. If we later want other peers to see
	/// the player anchored at the keyboard, add a [Networked] CurrentUser PlayerRef here.
	/// </summary>
	public sealed class Computer : InteractableStation
	{
		[Header("Camera")]
		[Tooltip("Pose the local camera lerps to when the player interacts. Author this transform on the prefab so it faces the monitor.")]
		public Transform ScreenViewTransform;

		[Tooltip("Seconds to ease from the player's normal camera pose into the screen view (and back out on close).")]
		[Min(0f)] public float ZoomDuration = 0.45f;

		[Tooltip("Easing applied to the 0..1 lerp parameter during zoom in/out. Default smoothstep-ish.")]
		public AnimationCurve ZoomEase = new AnimationCurve(
			new Keyframe(0f, 0f, 0f, 0f),
			new Keyframe(1f, 1f, 0f, 0f));

		protected override void OnInteract(InteractionScanner scanner)
		{
			if (ScreenViewTransform == null)
			{
				Debug.LogWarning($"[Computer:{name}] ScreenViewTransform not assigned — cannot open.", this);
				return;
			}

			var session = scanner.GetComponent<ComputerSession>();
			if (session != null) session.TryOpen(this);
		}
	}
}
