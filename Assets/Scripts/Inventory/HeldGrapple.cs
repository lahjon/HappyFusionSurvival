using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Local rope visual for the grapple gadget — the concrete <see cref="HeldGadget"/> for
	/// <see cref="GrappleGadgetCapability"/>. Attached to the held hand instance at equip and destroyed on
	/// deselect, so the rope only exists while the device is held.
	///
	/// This component owns no gameplay: the reel is networked state on <see cref="Player"/>
	/// (<see cref="Player.IsGrappling"/> / <see cref="Player.GrappleAnchor"/>), driven from the player's fire
	/// input. Here we just stretch a <see cref="LineRenderer"/> from the hand to the anchor whenever the owner
	/// is grappling. <see cref="OnRender"/> runs on every peer's instance, so remote players see the rope too —
	/// the networked anchor/flag replicate to proxies.
	/// </summary>
	public sealed class HeldGrapple : HeldGadget
	{
		private Color _ropeColor = new(0.85f, 0.7f, 0.4f, 1f);
		private float _ropeWidth = 0.03f;

		private LineRenderer _rope;

		/// <summary>Seed the rope tuning from authoring data and bring the gadget online. Called by
		/// <see cref="GrappleGadgetCapability.CreateRuntime"/> right after AddComponent, before the first Update.</summary>
		public void Initialize(GrappleGadgetCapability cap)
		{
			if (cap != null)
			{
				_ropeColor = cap.RopeColor;
				_ropeWidth = cap.RopeWidth;
			}
			Activate();
		}

		protected override void OnActivated()
		{
			var go = new GameObject("GrappleRope");
			go.transform.SetParent(transform, false);

			_rope = go.AddComponent<LineRenderer>();
			_rope.useWorldSpace = true;
			_rope.positionCount = 2;
			_rope.widthMultiplier = _ropeWidth;
			_rope.numCapVertices = 2;
			_rope.textureMode = LineTextureMode.Stretch;
			_rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			_rope.receiveShadows = false;
			// Unlit sprite shader tints purely via start/end color — no material asset dependency.
			_rope.material = new Material(Shader.Find("Sprites/Default"));
			_rope.startColor = _rope.endColor = _ropeColor;
			_rope.enabled = false;
		}

		protected override void OnRender()
		{
			if (_rope == null) return;

			bool show = OwnerPlayer != null && OwnerPlayer.IsGrappling;
			_rope.enabled = show;
			if (show == false) return;

			// Anchor the rope at the hand instance root (the launcher muzzle) and stretch it to the
			// networked anchor point. Both ends are world-space so it reads correctly on every viewer.
			_rope.SetPosition(0, transform.position);
			_rope.SetPosition(1, OwnerPlayer.GrappleAnchor);
		}
	}
}
