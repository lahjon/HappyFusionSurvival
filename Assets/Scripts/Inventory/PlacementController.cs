using Fusion;
using Starter.Common.Input;
using Starter.Common.Interactions;
using Starter.Common.Inventory;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only placement preview driver. Activates whenever the inventory is carrying a
	/// <see cref="PlaceableCapability"/>: instantiates a ghost from the def's GhostPrefab,
	/// raycasts from the camera, validates against the def's rules, tints the ghost green/red,
	/// and fires the inventory's RPC_RequestPlaceCarried on LMB.
	///
	/// Lives on the Player root next to <see cref="Inventory"/>. Runs only on input authority.
	/// Also acts as an <see cref="IInteractionGate"/>: while the player is carrying anything,
	/// the scanner is suppressed so world-space interaction prompts disappear and a stray
	/// Interact press can't open a chest, board a vehicle, etc. with your hands full.
	/// </summary>
	[RequireComponent(typeof(Inventory))]
	public sealed class PlacementController : MonoBehaviour, IInteractionGate
	{
		bool IInteractionGate.AllowInteractions => _inventory == null || _inventory.CarriedPlaceableId == 0;

		[Header("Ghost Materials (optional)")]
		[Tooltip("Material applied to the ghost when placement is valid. If null, a transparent green URP/Unlit material is generated at runtime.")]
		[SerializeField] private Material _validMaterial;
		[Tooltip("Material applied to the ghost when placement is invalid. If null, a transparent red URP/Unlit material is generated at runtime.")]
		[SerializeField] private Material _invalidMaterial;

		[Header("Camera")]
		[Tooltip("Transform used as the ray origin for the placement raycast. Typically the player's CameraHandle.")]
		[SerializeField] private Transform _rayOrigin;

		[Header("Tuning")]
		[Tooltip("Local cooldown (seconds) after firing a place RPC, so the slot decrement can replicate before the next attempt.")]
		[SerializeField] private float _postPlaceCooldown = 0.2f;
		[Tooltip("Layer to assign to the spawned ghost so it doesn't get picked up by gameplay raycasts. -1 = leave layer untouched.")]
		[SerializeField] private int _ghostLayer = -1;

		private Inventory _inventory;
		private GameInputActions _actions;
		private NetworkObject _netObject;

		private GameObject _ghost;
		private Renderer[] _ghostRenderers;
		private PlaceableCapability _ghostFor;
		private float _yawOffset;
		private float _pivotToBottomOffset;
		private bool _validThisFrame;
		private float _postPlaceCooldownLeft;

		private static readonly Collider[] s_overlapBuffer = new Collider[16];

		private const float SnapDownRange = 1f;

		private void Awake()
		{
			_inventory = GetComponent<Inventory>();
			_actions = GetComponent<GameInputActions>();
			_netObject = GetComponent<NetworkObject>();
		}

		private void Update()
		{
			if (_netObject == null || _netObject.HasInputAuthority == false)
			{
				TeardownGhost();
				return;
			}

			var def = _inventory.CarriedPlaceable;
			if (def == null || def.PlacedPrefab == null)
			{
				TeardownGhost();
				return;
			}

			if (Cursor.lockState != CursorLockMode.Locked)
			{
				// While the player is in a menu/loot UI etc. don't show the ghost.
				TeardownGhost();
				return;
			}

			EnsureGhost(def);

			if (_postPlaceCooldownLeft > 0f)
				_postPlaceCooldownLeft -= Time.deltaTime;

			if (_actions != null && _actions.IsInitialized && _actions.Rotate.IsPressed())
			{
				_yawOffset += def.RotationSpeed * Time.deltaTime;
			}

			UpdateGhostPose(def);

			if (_actions != null && _actions.IsInitialized && _actions.Fire.WasPressedThisFrame())
			{
				TryConfirmPlacement(def);
			}
		}

		private void OnDisable()
		{
			TeardownGhost();
		}

		private void TryConfirmPlacement(PlaceableCapability def)
		{
			if (_validThisFrame == false) return;
			if (_postPlaceCooldownLeft > 0f) return;

			_inventory.RequestPlaceCarried(_ghost.transform.position, _ghost.transform.rotation);
			_postPlaceCooldownLeft = _postPlaceCooldown;
		}

		private void EnsureGhost(PlaceableCapability def)
		{
			if (_ghost != null && _ghostFor == def) return;

			TeardownGhost();

			var prefab = def.GhostPrefab != null ? def.GhostPrefab : def.PlacedPrefab;
			if (prefab == null) return;

			_ghost = Instantiate(prefab);
			_ghost.name = $"PlacementGhost_{_inventory.CarriedDefinition?.DisplayName}";
			StripGhostComponents(_ghost);

			_ghostRenderers = _ghost.GetComponentsInChildren<Renderer>(true);
			_ghostFor = def;
			_yawOffset = 0f;

			// Measure how far below the pivot the renderer bounds reach, so we can
			// snap the bottom of the ghost (not its pivot) to the surface.
			_ghost.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
			_pivotToBottomOffset = ComputePivotToBottomOffset(_ghostRenderers);

			_inventory.SuppressHeldVisual = true;
		}

		private void TeardownGhost()
		{
			if (_ghost != null)
			{
				Destroy(_ghost);
				_ghost = null;
			}
			_ghostRenderers = null;
			_ghostFor = null;
			_validThisFrame = false;

			if (_inventory != null && _inventory.SuppressHeldVisual)
				_inventory.SuppressHeldVisual = false;
		}

		// Strip everything that would make the ghost interact with the world: physics,
		// networking, and any author-time gameplay components. The ghost is a pure visual.
		private void StripGhostComponents(GameObject go)
		{
			foreach (var col in go.GetComponentsInChildren<Collider>(true))
				col.enabled = false;
			foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
			{
				rb.isKinematic = true;
				rb.detectCollisions = false;
			}

			// Disable any Fusion behaviours so they don't try to read a missing runner.
			foreach (var nb in go.GetComponentsInChildren<NetworkBehaviour>(true))
				nb.enabled = false;
			var netObj = go.GetComponent<NetworkObject>();
			if (netObj != null) netObj.enabled = false;

			if (_ghostLayer >= 0)
				SetLayerRecursively(go, _ghostLayer);
		}

		private static void SetLayerRecursively(GameObject go, int layer)
		{
			go.layer = layer;
			var t = go.transform;
			for (int i = 0; i < t.childCount; i++)
				SetLayerRecursively(t.GetChild(i).gameObject, layer);
		}

		// Ghost is at origin/identity here, so renderer world bounds == ghost-local bounds.
		// Returns the distance from pivot down to the lowest point of the combined bounds.
		private static float ComputePivotToBottomOffset(Renderer[] renderers)
		{
			if (renderers == null || renderers.Length == 0) return 0f;

			bool initialized = false;
			Bounds b = default;
			for (int i = 0; i < renderers.Length; i++)
			{
				var r = renderers[i];
				if (r == null) continue;
				if (initialized == false) { b = r.bounds; initialized = true; }
				else b.Encapsulate(r.bounds);
			}
			if (initialized == false) return 0f;
			return Mathf.Max(0f, -b.min.y);
		}

		private void UpdateGhostPose(PlaceableCapability def)
		{
			if (_ghost == null) return;
			if (_rayOrigin == null)
			{
				_validThisFrame = false;
				ApplyTint(false);
				return;
			}

			Vector3 origin = _rayOrigin.position;
			Vector3 forward = _rayOrigin.forward;

			bool hit = Physics.Raycast(origin, forward, out RaycastHit info, def.PlacementRange, def.PlacementMask, QueryTriggerInteraction.Ignore);

			Vector3 surfacePoint;
			Vector3 normal;
			bool surfaceOk;
			Collider surfaceCollider = null;

			if (hit)
			{
				surfacePoint = info.point;
				normal = info.normal;
				surfaceCollider = info.collider;
				var kind = def.ClassifySurface(normal);
				surfaceOk = def.SurfaceAllowed(kind);
			}
			else
			{
				// No surface in range — float the ghost at max range so the player sees where they're aiming.
				surfacePoint = origin + forward * def.PlacementRange;
				normal = Vector3.up;
				surfaceOk = false;
			}

			// Snap to a placement-mask surface directly below the current position (within 1m),
			// so placeables prefer the floor when aiming slightly above it or into walls.
			Vector3 snapStart = surfacePoint + Vector3.up * 0.05f;
			if (Physics.Raycast(snapStart, Vector3.down, out RaycastHit snapHit, SnapDownRange + 0.05f, def.PlacementMask, QueryTriggerInteraction.Ignore))
			{
				surfacePoint = snapHit.point;
				normal = snapHit.normal;
				surfaceCollider = snapHit.collider;
				var kind = def.ClassifySurface(normal);
				surfaceOk = def.SurfaceAllowed(kind);
				hit = true;
			}

			Quaternion rot = ComputeRotation(forward, normal, def);

			Vector3 upAxis = def.AlignToSurface ? normal : Vector3.up;
			Vector3 pos = surfacePoint + upAxis * _pivotToBottomOffset;

			_ghost.transform.SetPositionAndRotation(pos, rot);

			// Center the clearance sphere just above the surface so its lower edge sits at floor level.
			// Otherwise the sphere intersects the floor and tiled ground (e.g. PolygonTown's many
			// adjacent road/sidewalk colliders) reports the spot as blocked — we can only mask out
			// the single collider returned by the raycast, not its neighbours.
			Vector3 clearanceCenter = surfacePoint + upAxis * (def.Footprint + 0.02f);
			bool clear = def.Footprint <= 0f || IsClearance(clearanceCenter, def.Footprint, def.PlacementMask, surfaceCollider);
			_validThisFrame = hit && surfaceOk && clear;
			ApplyTint(_validThisFrame);
		}

		// Object up axis = surface normal (when AlignToSurface) or world up. Yaw around that
		// axis is composed from (a) the camera direction projected onto the surface plane,
		// so the ghost initially faces the player, and (b) the held-R offset.
		private Quaternion ComputeRotation(Vector3 cameraForward, Vector3 normal, PlaceableCapability def)
		{
			Vector3 up = def.AlignToSurface ? normal : Vector3.up;

			Vector3 forward = Vector3.ProjectOnPlane(-cameraForward, up);
			if (forward.sqrMagnitude < 0.0001f)
				forward = Vector3.ProjectOnPlane(Vector3.forward, up);
			if (forward.sqrMagnitude < 0.0001f)
				forward = Vector3.ProjectOnPlane(Vector3.right, up);
			forward.Normalize();

			Quaternion baseRot = Quaternion.LookRotation(forward, up);
			return Quaternion.AngleAxis(_yawOffset, up) * baseRot;
		}

		private bool IsClearance(Vector3 position, float radius, int layerMask, Collider surfaceCollider)
		{
			int count = Physics.OverlapSphereNonAlloc(position, radius, s_overlapBuffer, layerMask, QueryTriggerInteraction.Ignore);
			Transform self = transform.root;
			for (int i = 0; i < count; i++)
			{
				var col = s_overlapBuffer[i];
				if (col == null) continue;
				if (col == surfaceCollider) continue;
				if (col.transform.root == self) continue;
				return false;
			}
			return true;
		}

		private void ApplyTint(bool valid)
		{
			if (_ghostRenderers == null) return;
			Material mat = valid ? GetValidMaterial() : GetInvalidMaterial();
			if (mat == null) return;

			for (int i = 0; i < _ghostRenderers.Length; i++)
			{
				var r = _ghostRenderers[i];
				if (r == null) continue;
				var mats = r.sharedMaterials;
				bool changed = false;
				for (int m = 0; m < mats.Length; m++)
				{
					if (mats[m] != mat) { mats[m] = mat; changed = true; }
				}
				if (changed) r.sharedMaterials = mats;
				r.shadowCastingMode = ShadowCastingMode.Off;
			}
		}

		private Material GetValidMaterial()
		{
			if (_validMaterial != null) return _validMaterial;
			_validMaterial = BuildGhostMaterial(new Color(0.2f, 1f, 0.3f, 0.45f));
			return _validMaterial;
		}

		private Material GetInvalidMaterial()
		{
			if (_invalidMaterial != null) return _invalidMaterial;
			_invalidMaterial = BuildGhostMaterial(new Color(1f, 0.25f, 0.25f, 0.45f));
			return _invalidMaterial;
		}

		private static Material BuildGhostMaterial(Color color)
		{
			var shader = Shader.Find("Universal Render Pipeline/Unlit");
			if (shader == null) shader = Shader.Find("Unlit/Color");
			if (shader == null) return null;

			var mat = new Material(shader);
			mat.SetColor("_BaseColor", color);
			mat.SetColor("_Color", color);

			// URP Unlit transparency setup (silently no-op on non-URP shaders that don't have these props).
			if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
			if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
			if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
			if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
			mat.SetOverrideTag("RenderType", "Transparent");
			mat.renderQueue = (int)RenderQueue.Transparent;
			mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
			mat.DisableKeyword("_ALPHATEST_ON");

			return mat;
		}
	}
}
