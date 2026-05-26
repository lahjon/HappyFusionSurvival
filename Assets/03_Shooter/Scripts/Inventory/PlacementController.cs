using Fusion;
using Starter.Common.Input;
using Starter.Common.Inventory;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only placement preview driver. Activates whenever the inventory's selected
	/// item is a <see cref="PlaceableDefinition"/>: instantiates a ghost from the def's
	/// GhostPrefab, raycasts from the camera, validates against the def's rules,
	/// tints the ghost green/red, and fires the inventory's RPC_RequestPlace on LMB.
	///
	/// Lives on the Player root next to <see cref="Inventory"/>. Runs only on input authority.
	/// </summary>
	[RequireComponent(typeof(Inventory))]
	public sealed class PlacementController : MonoBehaviour
	{
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
		private PlaceableDefinition _ghostFor;
		private float _yawOffset;
		private bool _validThisFrame;
		private float _postPlaceCooldownLeft;

		private static readonly Collider[] s_overlapBuffer = new Collider[16];

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

			var def = _inventory.SelectedDefinition as PlaceableDefinition;
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

		private void TryConfirmPlacement(PlaceableDefinition def)
		{
			if (_validThisFrame == false) return;
			if (_postPlaceCooldownLeft > 0f) return;

			_inventory.RequestPlaceSelected(_ghost.transform.position, _ghost.transform.rotation);
			_postPlaceCooldownLeft = _postPlaceCooldown;
		}

		private void EnsureGhost(PlaceableDefinition def)
		{
			if (_ghost != null && _ghostFor == def) return;

			TeardownGhost();

			var prefab = def.GhostPrefab != null ? def.GhostPrefab : def.PlacedPrefab;
			if (prefab == null) return;

			_ghost = Instantiate(prefab);
			_ghost.name = $"PlacementGhost_{def.DisplayName}";
			StripGhostComponents(_ghost);

			_ghostRenderers = _ghost.GetComponentsInChildren<Renderer>(true);
			_ghostFor = def;
			_yawOffset = 0f;

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

		private void UpdateGhostPose(PlaceableDefinition def)
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

			Vector3 pos;
			Vector3 normal;
			bool surfaceOk;

			if (hit)
			{
				pos = info.point;
				normal = info.normal;
				var kind = def.ClassifySurface(normal);
				surfaceOk = def.SurfaceAllowed(kind);
			}
			else
			{
				// No surface in range — float the ghost at max range so the player sees where they're aiming.
				pos = origin + forward * def.PlacementRange;
				normal = Vector3.up;
				surfaceOk = false;
			}

			Quaternion rot = ComputeRotation(forward, normal, def);
			_ghost.transform.SetPositionAndRotation(pos, rot);

			bool clear = def.Footprint <= 0f || IsClearance(pos, def.Footprint);
			_validThisFrame = hit && surfaceOk && clear;
			ApplyTint(_validThisFrame);
		}

		// Object up axis = surface normal (when AlignToSurface) or world up. Yaw around that
		// axis is composed from (a) the camera direction projected onto the surface plane,
		// so the ghost initially faces the player, and (b) the held-R offset.
		private Quaternion ComputeRotation(Vector3 cameraForward, Vector3 normal, PlaceableDefinition def)
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

		private bool IsClearance(Vector3 position, float radius)
		{
			int count = Physics.OverlapSphereNonAlloc(position, radius, s_overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
			Transform self = transform.root;
			for (int i = 0; i < count; i++)
			{
				var col = s_overlapBuffer[i];
				if (col == null) continue;
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
