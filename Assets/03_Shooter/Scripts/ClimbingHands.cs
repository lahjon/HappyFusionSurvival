using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only procedural visual: two sphere "hands" placed on the climbing surface while
	/// IsClimbing or IsMantling is active. Auto-spawns its own primitive sphere children — no
	/// prefab edits required.
	///
	/// Each hand plants at a world-space anchor and stays still while the body moves. When the
	/// anchor drifts far enough from where the hand "should" be (chest + lateral offset), the
	/// hand reaches to a new anchor with an arcing animation. Only one hand reaches at a time,
	/// so the cadence naturally alternates as the player climbs.
	///
	/// During a mantle the planting system is bypassed in favour of a fixed two-phase pull-up.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class ClimbingHands : MonoBehaviour
	{
		[Header("Hand visuals")]
		[Tooltip("Diameter of each hand sphere (meters).")]
		public float HandSize = 0.12f;
		[Tooltip("Distance between hands when planted (meters).")]
		public float ShoulderWidth = 0.45f;
		[Tooltip("Vertical offset of each hand above the chest along the wall surface (meters). Raise this to bring the hands into the FPS camera's view.")]
		public float HandHeightAboveChest = 0.55f;
		[Tooltip("Distance from wall surface the hands rest (meters). Keeps spheres from z-fighting with the wall.")]
		public float HandStandoff = 0.03f;
		[Tooltip("Base color used for the auto-created hand material.")]
		public Color HandColor = new Color(0.92f, 0.74f, 0.58f);

		[Header("Plant + reach")]
		[Tooltip("How far an anchor can drift from the chest's ideal position before that hand re-plants. Larger values give wider, slower steps.")]
		public float ReachTriggerDistance = 0.35f;
		[Tooltip("How far ahead-of-chest (in the direction of motion) each new anchor is placed. Creates the staircase pattern when climbing up.")]
		public float ReachLeadDistance = 0.18f;
		[Tooltip("Seconds a single reach animation takes from start to plant.")]
		public float ReachDuration = 0.22f;
		[Tooltip("How far the hand lifts off the wall at the apex of a reach (meters).")]
		public float ReachArcHeight = 0.06f;

		[Header("Mantle pull-up")]
		[Tooltip("How far above the ledge the hands grip during the first half of a mantle (meters).")]
		public float MantleGrabHeight = 0.1f;
		[Tooltip("How far forward of the mantle end position the hands plant during a mantle (meters).")]
		public float MantleGrabForward = 0.15f;
		[Tooltip("How quickly the visual hands snap toward their target positions during mantle.")]
		public float MantleFollowSpeed = 18f;

		private struct HandState
		{
			public Vector3 Anchor;
			public Vector3 ReachFrom;
			public Vector3 ReachTo;
			public float ReachProgress; // 1 = planted, 0..1 = animating
			public bool Initialized;
		}

		private Player _player;
		private Transform _leftHand;
		private Transform _rightHand;
		private Renderer _leftRenderer;
		private Renderer _rightRenderer;
		private Material _handMaterial;

		private HandState _left;
		private HandState _right;
		private Vector3 _lastChest;
		private bool _layerAssigned;

		private void Awake()
		{
			_player = GetComponent<Player>();
			CreateHandMaterial();
			CreateHands();
		}

		private void OnDestroy()
		{
			// Spheres are unparented children — clean them up.
			if (_leftHand != null) Destroy(_leftHand.gameObject);
			if (_rightHand != null) Destroy(_rightHand.gameObject);
			if (_handMaterial != null) Destroy(_handMaterial);
		}

		private void CreateHandMaterial()
		{
			// URP project — Lit gives proper lighting response. Fall back through a couple of
			// shader names in case the render pipeline switches, then surrender to the default.
			var shader = Shader.Find("Universal Render Pipeline/Lit");
			if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
			if (shader == null) shader = Shader.Find("Standard");
			if (shader == null) return;

			_handMaterial = new Material(shader) { color = HandColor };
			// URP Lit reads "_BaseColor" rather than "_Color"; set both so the material renders the
			// requested color regardless of which shader actually resolved.
			if (_handMaterial.HasProperty("_BaseColor"))
			{
				_handMaterial.SetColor("_BaseColor", HandColor);
			}
		}

		private void CreateHands()
		{
			_leftHand = CreateHand("ClimbHand_Left");
			_rightHand = CreateHand("ClimbHand_Right");
			_leftRenderer = _leftHand.GetComponent<Renderer>();
			_rightRenderer = _rightHand.GetComponent<Renderer>();
			if (_handMaterial != null)
			{
				_leftRenderer.sharedMaterial = _handMaterial;
				_rightRenderer.sharedMaterial = _handMaterial;
			}
			SetVisible(false);
		}

		private Transform CreateHand(string objName)
		{
			var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			sphere.name = objName;
			var col = sphere.GetComponent<Collider>();
			if (col != null) Destroy(col);
			sphere.transform.localScale = Vector3.one * HandSize;
			// Detached so the Player's scale-squash on jump can't warp the spheres.
			sphere.transform.SetParent(null, true);
			return sphere.transform;
		}

		private void SetVisible(bool visible)
		{
			if (_leftRenderer != null) _leftRenderer.enabled = visible;
			if (_rightRenderer != null) _rightRenderer.enabled = visible;
		}

		private void LateUpdate()
		{
			if (_player == null || _leftHand == null || _rightHand == null) return;

			EnsureLayer();

			bool visible = _player.IsClimbing || _player.IsMantling;
			SetVisible(visible);
			if (!visible)
			{
				_left.Initialized = false;
				_right.Initialized = false;
				return;
			}

			if (_player.IsMantling)
			{
				UpdateMantleHands();
			}
			else
			{
				UpdateClimbHands();
			}
		}

		// Defer FirstPersonOverlay layer assignment until the NetworkObject is spawned so
		// HasInputAuthority returns a valid value. Idempotent — only runs once after spawn.
		private void EnsureLayer()
		{
			if (_layerAssigned) return;
			if (_player == null || _player.Object == null) return;

			if (_player.HasInputAuthority)
			{
				int overlay = LayerMask.NameToLayer("FirstPersonOverlay");
				if (overlay >= 0)
				{
					_leftHand.gameObject.layer = overlay;
					_rightHand.gameObject.layer = overlay;
				}
			}
			// Proxies (remote players) stay on the default layer so the hands occlude normally
			// against the wall geometry from third-person observer cameras.
			_layerAssigned = true;
		}

		private void UpdateClimbHands()
		{
			Vector3 wallNormal = _player.ClimbWallNormal;
			if (wallNormal.sqrMagnitude < 0.0001f) return;

			// Surface basis matching Player.cs convention — right is the player's actual right
			// when facing the wall (positive X for a wall facing -Z, etc.).
			Vector3 surfaceRight = Vector3.Cross(wallNormal, Vector3.up);
			if (surfaceRight.sqrMagnitude < 0.0001f) return;
			surfaceRight.Normalize();
			Vector3 surfaceUp = Vector3.Cross(surfaceRight, wallNormal).normalized;

			Transform chestBone = _player.ChestBone;
			Vector3 chest = chestBone != null ? chestBone.position : _player.transform.position + Vector3.up;

			// Ideal positions (where each hand "wants" to be, relative to the chest's current pose).
			// Plant-and-reach logic compares each hand's anchor to its ideal — when the gap exceeds
			// ReachTriggerDistance, that hand re-plants. Lifted above chest along the wall surface
			// so the hands sit in the FPS camera's view.
			Vector3 chestOnWall = chest + surfaceUp * HandHeightAboveChest;
			Vector3 leftIdeal = chestOnWall - surfaceRight * (ShoulderWidth * 0.5f) + wallNormal * HandStandoff;
			Vector3 rightIdeal = chestOnWall + surfaceRight * (ShoulderWidth * 0.5f) + wallNormal * HandStandoff;

			// First frame after entering climb: snap anchors to ideals so we don't animate from a stale position.
			if (!_left.Initialized)
			{
				_left.Anchor = leftIdeal;
				_left.ReachProgress = 1f;
				_left.Initialized = true;
			}
			if (!_right.Initialized)
			{
				_right.Anchor = rightIdeal;
				_right.ReachProgress = 1f;
				_right.Initialized = true;
				_lastChest = chest;
			}

			// Chest movement direction projected onto the wall plane drives the lead distance,
			// so each fresh anchor lands slightly ahead of the body, in the direction of motion.
			Vector3 chestDelta = chest - _lastChest;
			Vector3 moveOnWall = chestDelta - Vector3.Project(chestDelta, wallNormal);
			Vector3 moveDir = moveOnWall.sqrMagnitude > 0.0001f ? moveOnWall.normalized : Vector3.zero;
			_lastChest = chest;

			// Decide whether to start a new reach. Only one hand reaches at a time so the cadence
			// stays clean (BOTW-style alternation).
			bool eitherReaching = _left.ReachProgress < 1f || _right.ReachProgress < 1f;
			if (!eitherReaching)
			{
				float leftDist = Vector3.Distance(_left.Anchor, leftIdeal);
				float rightDist = Vector3.Distance(_right.Anchor, rightIdeal);
				float maxDist = Mathf.Max(leftDist, rightDist);
				if (maxDist > ReachTriggerDistance)
				{
					if (leftDist >= rightDist)
					{
						StartReach(ref _left, leftIdeal + moveDir * ReachLeadDistance);
					}
					else
					{
						StartReach(ref _right, rightIdeal + moveDir * ReachLeadDistance);
					}
				}
			}

			// Advance reach animations and write final visual positions.
			_leftHand.position = AdvanceHand(ref _left, wallNormal);
			_rightHand.position = AdvanceHand(ref _right, wallNormal);
		}

		private static void StartReach(ref HandState hand, Vector3 target)
		{
			hand.ReachFrom = hand.Anchor;
			hand.ReachTo = target;
			hand.ReachProgress = 0f;
		}

		private Vector3 AdvanceHand(ref HandState hand, Vector3 wallNormal)
		{
			if (hand.ReachProgress >= 1f)
			{
				return hand.Anchor;
			}

			hand.ReachProgress = Mathf.Clamp01(hand.ReachProgress + Time.deltaTime / Mathf.Max(0.01f, ReachDuration));

			// SmoothStep gives the reach a gentle ease in/out — the hand decelerates as it lands.
			float t = Mathf.SmoothStep(0f, 1f, hand.ReachProgress);
			Vector3 along = Vector3.Lerp(hand.ReachFrom, hand.ReachTo, t);
			// Arc the hand off the wall using a sin curve — peaks at progress=0.5, zero at the endpoints.
			float arc = Mathf.Sin(hand.ReachProgress * Mathf.PI) * ReachArcHeight;
			Vector3 pos = along + wallNormal * arc;

			if (hand.ReachProgress >= 1f)
			{
				hand.Anchor = hand.ReachTo;
			}
			return pos;
		}

		private void UpdateMantleHands()
		{
			float progress = _player.MantleProgress;
			Vector3 start = _player.MantleStart;
			Vector3 end = _player.MantleEnd;

			Vector3 forward = end - start;
			forward.y = 0f;
			if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;
			forward.Normalize();
			Vector3 lateral = Vector3.Cross(Vector3.up, forward).normalized;

			// Ledge grip target: above + forward of the mantle end so the spheres read as
			// hands gripping the lip of the ledge rather than sunk into the surface.
			Vector3 grip = end + Vector3.up * MantleGrabHeight + forward * MantleGrabForward;
			// Phase 1 (0..0.5): hands stay planted on the lip while the body rises.
			// Phase 2 (0.5..1): hands ease down to the standing surface as the body comes level.
			float phase2 = Mathf.Clamp01((progress - 0.5f) * 2f);
			Vector3 anchor = Vector3.Lerp(grip, end + Vector3.up * 0.05f, phase2);

			Vector3 leftTarget = anchor - lateral * (ShoulderWidth * 0.5f);
			Vector3 rightTarget = anchor + lateral * (ShoulderWidth * 0.5f);

			// Mantle uses a simple lerp toward the target — the planted-anchor logic doesn't apply
			// because the body is in a forced animation rather than free climbing.
			float t = 1f - Mathf.Exp(-MantleFollowSpeed * Time.deltaTime);
			Vector3 leftPos = Vector3.Lerp(_leftHand.position, leftTarget, t);
			Vector3 rightPos = Vector3.Lerp(_rightHand.position, rightTarget, t);

			_leftHand.position = leftPos;
			_rightHand.position = rightPos;

			// Keep the anchors in sync with the mantle end position so the first post-mantle frame
			// (back in free climb / normal movement) doesn't snap from a stale spot.
			_left.Anchor = leftPos;
			_right.Anchor = rightPos;
			_left.ReachProgress = 1f;
			_right.ReachProgress = 1f;
			_left.Initialized = true;
			_right.Initialized = true;
		}
	}
}
