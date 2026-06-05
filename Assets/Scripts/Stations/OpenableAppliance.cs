using System.Collections.Generic;
using Fusion;
using Starter.Common.Interactions;
using UnityEngine;
using UnityEngine.Events;

namespace Starter.Shooter
{
	/// <summary>
	/// Abstract base for any networked appliance with a hinged door — fridge, pantry, chest,
	/// cabinet, oven, microwave. Owns everything door-related so the leaves don't repeat it:
	///
	/// - <b>Open/close</b>: one <c>[Networked] NetworkBool</c> <see cref="IsOpen"/> driven by a
	///   state-authority RPC (host range-validated), the door swing lerped locally toward the
	///   open/closed pose, and open/close one-shot audio — the <see cref="Door"/> + <see cref="Microwave"/> pattern.
	/// - <b>Interior light</b> on while <see cref="LightShouldBeOn"/> (open by default; a cooking
	///   appliance also lights while running).
	/// - <b>Interior gating</b>: while the door is shut, the <see cref="InteractionScanner"/> skips
	///   any interactable whose point sits inside <see cref="_interiorVolume"/> (see
	///   <see cref="IsPointAccessible"/>) — so the contents can't be reached until it's opened.
	///   Evaluated live per-peer off <see cref="IsOpen"/>; contents persist (never despawned).
	///
	/// Two interaction styles, picked by <see cref="_doorIsInteractable"/>:
	/// - ON (fridge, oven): the door itself is the <see cref="IInteractable"/> — aim at it and interact.
	/// - OFF (microwave, radio-style): the body isn't interactable; a physical button child forwards
	///   to the public <see cref="RequestToggleOpen"/> (and a cooking appliance's cook toggle).
	///
	/// Leaves: <see cref="ContainerOpenable"/> (plain container) and <see cref="CookingAppliance"/>
	/// (adds a heat/cook cycle). Subclasses override the protected hooks to layer behaviour on the
	/// single networked open flag; they must call <c>base.Spawned()</c> / <c>base.Despawned()</c>.
	/// </summary>
	// No [RequireComponent(NetworkObject)]: an appliance's NetworkObject may live on an ANCESTOR, not this
	// GameObject — e.g. a multi-door fridge puts one ContainerOpenable per door (child) all sharing the
	// root's single NetworkObject. Ensure the prefab root (or an ancestor) carries a NetworkObject.
	public abstract class OpenableAppliance : NetworkBehaviour, IInteractable, IInteractionPromptAnchor
	{
		// Every spawned appliance registers here so the local InteractionScanner can ask whether a
		// candidate point is sealed inside a closed one — without depending on this type's internals.
		// Static, local-only; entries added in Spawned and removed in Despawned/OnDisable.
		private static readonly List<OpenableAppliance> s_all = new List<OpenableAppliance>();

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics() => s_all.Clear();

		[Header("Interaction")]
		[Tooltip("Max distance from the local player at which the door can be opened/closed.")]
		[Min(0f)] [SerializeField] private float _interactRange = 2.5f;
		[Tooltip("ON: the door itself is the interactable (fridge/oven). OFF: the body isn't interactable and a " +
		         "button child drives RequestToggleOpen() instead (microwave).")]
		[SerializeField] private bool _doorIsInteractable = true;
		[Tooltip("Point used for range checks and the prompt anchor — usually the door/handle. MUST sit OUTSIDE " +
		         "the interior volume so the door itself stays reachable while shut. Falls back to this transform.")]
		[SerializeField] private Transform _interactionPoint;
		[Tooltip("Prompt label shown while closed.")]
		[SerializeField] private string _openLabel = "Open";
		[Tooltip("Prompt label shown while open.")]
		[SerializeField] private string _closeLabel = "Close";

		[Header("Door")]
		[Tooltip("Door transform that swings on its hinge. Lerped locally toward the open/closed pose.")]
		[SerializeField] private Transform _door;
		[Tooltip("Local euler angles of the door when closed.")]
		[SerializeField] private Vector3 _doorClosedEuler = Vector3.zero;
		[Tooltip("Local euler angles of the door when open.")]
		[SerializeField] private Vector3 _doorOpenEuler = new Vector3(0f, -110f, 0f);
		[Tooltip("How fast the door swings toward its target pose.")]
		[Min(0.1f)] [SerializeField] private float _doorLerpSpeed = 10f;
		[Tooltip("Extra doors/lids that open together with the main door (e.g. a fridge's second door, a double " +
		         "cabinet). Each has its own closed/open angles; they share the lerp speed and toggle as one.")]
		[SerializeField] private ApplianceDoor[] _extraDoors;

		[Header("Interior")]
		[Tooltip("Box marking the interior space. While shut, the InteractionScanner skips any interactable whose " +
		         "point is inside it — the 'can't touch the contents until it's open' rule. A trigger BoxCollider is " +
		         "ideal; leave null to gate nothing (door + light only, e.g. a microwave).")]
		[SerializeField] private BoxCollider _interiorVolume;
		[Tooltip("Interior light, on while LightShouldBeOn (open by default).")]
		[SerializeField] protected Light _light;
		[Tooltip("ON (fridge/oven): the interior light comes on whenever the door is open. OFF (microwave): the " +
		         "light only lights while the appliance is running, not when the door opens.")]
		[SerializeField] private bool _lightWhenOpen = true;

		[Header("Audio")]
		[Tooltip("One-shot clip played when the door opens.")]
		[SerializeField] private AudioClip _openClip;
		[Tooltip("One-shot clip played when the door closes.")]
		[SerializeField] private AudioClip _closeClip;
		[Range(0f, 1f)] [SerializeField] private float _doorVolume = 1f;

		[Header("Authority")]
		[Tooltip("Host re-validates the requesting player is within this range before applying a request. 0 = skip the check.")]
		[Min(0f)] [SerializeField] private float _hostValidationRange = 2.5f;

		[Header("Events")]
		[Tooltip("Fires locally on every peer when the door finishes toggling (true = opened). Wire VFX, extra SFX, etc.")]
		[SerializeField] private UnityEvent<bool> _onOpenChanged;

		/// <summary>True while the door is open. State authority writes; peers render via OnChangedRender.</summary>
		[Networked, OnChangedRender(nameof(OnOpenRender))]
		public NetworkBool IsOpen { get; private set; }

		// Local mirror of IsOpen so Update() (which runs before Spawned() and on the editor instance)
		// can lerp the door without touching the networked property, which throws until spawned.
		private bool _openLocal;
		private bool _registered;

		/// <summary>World point used for interaction range, host validation, and audio. The door/handle if wired.</summary>
		protected Vector3 InteractPoint => _interactionPoint != null ? _interactionPoint.position : transform.position;

		// ── IInteractable ────────────────────────────────────────────────────────
		float IInteractable.InteractRange => _interactRange;
		bool IInteractable.CanInteract => _doorIsInteractable;
		Vector3 IInteractable.InteractionPoint => InteractPoint;
		string IInteractable.LockedReason => null;
		string IInteractable.InteractLabel => IsOpen ? _closeLabel : _openLabel;
		Transform IInteractionPromptAnchor.PromptAnchor => _interactionPoint;

		void IInteractable.OnInteract(InteractionScanner scanner) => RequestToggleOpen();

		protected virtual void Reset()
		{
			_light = GetComponentInChildren<Light>(true);
		}

		public override void Spawned()
		{
			_openLocal = IsOpen;
			ApplyLight();
			SnapDoor();
			Register();
		}

		public override void Despawned(NetworkRunner runner, bool hasState) => Unregister();
		protected virtual void OnDisable() => Unregister();

		private void Register()
		{
			if (_registered) return;
			s_all.Add(this);
			_registered = true;
		}

		private void Unregister()
		{
			if (!_registered) return;
			s_all.Remove(this);
			_registered = false;
		}

		// ── Open/close ─────────────────────────────────────────────────────────────
		/// <summary>Toggle the door open/closed. Public so a button child can drive it; sends a state-authority RPC.</summary>
		public void RequestToggleOpen() => RPC_RequestToggleOpen();

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_RequestToggleOpen(RpcInfo info = default)
		{
			if (!ValidateSource(info)) return;
			IsOpen = !IsOpen;
			OnOpenToggledAuthority(); // e.g. opening a cooking appliance aborts the cook
		}

		/// <summary>State-authority hook fired right after <see cref="IsOpen"/> is flipped by a request. Default no-op.</summary>
		protected virtual void OnOpenToggledAuthority() { }

		/// <summary>
		/// Shared host re-validation: true if the requesting player is still within
		/// <see cref="_hostValidationRange"/> (×1.25) of <see cref="InteractPoint"/>.
		/// </summary>
		protected bool ValidateSource(RpcInfo info)
		{
			if (Runner == null) return false;
			if (_hostValidationRange <= 0f) return true;

			var src = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
			var playerObj = Runner.GetPlayerObject(src);
			if (playerObj == null) return false;

			float allowed = _hostValidationRange * 1.25f;
			return (InteractPoint - playerObj.transform.position).sqrMagnitude <= allowed * allowed;
		}

		// ── Interior gating ─────────────────────────────────────────────────────────
		/// <summary>
		/// Local-only query used by the <see cref="InteractionScanner"/>: false when <paramref name="point"/>
		/// is sealed inside a closed appliance, so its contents can't be reached until the door is opened.
		/// Cheap — a handful of appliances, an AABB-in-OBB test each. Safe before anything spawns (empty → true).
		/// </summary>
		public static bool IsPointAccessible(Vector3 point)
		{
			for (int i = 0; i < s_all.Count; i++)
			{
				var a = s_all[i];
				if (a != null && a.SealsPoint(point)) return false;
			}
			return true;
		}

		// True if the door is closed and the point lies within the interior box (tested in the box's
		// local space so a rotated appliance still gates correctly).
		private bool SealsPoint(Vector3 worldPoint)
		{
			if (_interiorVolume == null) return false;
			// Object may be despawning the frame the scanner queries — reading IsOpen then would throw.
			if (Object == null || !Object.IsValid) return false;
			if (IsOpen) return false;

			Vector3 local = _interiorVolume.transform.InverseTransformPoint(worldPoint) - _interiorVolume.center;
			Vector3 half = _interiorVolume.size * 0.5f;
			return Mathf.Abs(local.x) <= half.x
			    && Mathf.Abs(local.y) <= half.y
			    && Mathf.Abs(local.z) <= half.z;
		}

		// ── Render ───────────────────────────────────────────────────────────────
		protected virtual void Update()
		{
			float t = _doorLerpSpeed * Time.deltaTime;
			LerpDoor(_door, _doorClosedEuler, _doorOpenEuler, t);
			if (_extraDoors != null)
				for (int i = 0; i < _extraDoors.Length; i++)
					LerpDoor(_extraDoors[i].Door, _extraDoors[i].ClosedEuler, _extraDoors[i].OpenEuler, t);
		}

		private void LerpDoor(Transform door, Vector3 closedEuler, Vector3 openEuler, float t)
		{
			if (door == null) return;
			Quaternion target = Quaternion.Euler(_openLocal ? openEuler : closedEuler);
			door.localRotation = Quaternion.Slerp(door.localRotation, target, t);
		}

		private void OnOpenRender()
		{
			_openLocal = IsOpen; // Update() lerps the door pose from the local mirror
			ApplyLight();

			var clip = IsOpen ? _openClip : _closeClip;
			if (clip != null)
				AudioManager.Instance?.PlaySFX(clip, InteractPoint, _doorVolume);

			_onOpenChanged?.Invoke(IsOpen);
			OnOpenStateRendered(IsOpen);
		}

		/// <summary>Render-side hook fired on every peer after the door's open state changes. Default no-op.</summary>
		protected virtual void OnOpenStateRendered(bool isOpen) { }

		/// <summary>Whether the interior light should be lit. Open by default; cooking appliances also light while running.</summary>
		protected virtual bool LightShouldBeOn => _lightWhenOpen && IsOpen;

		/// <summary>Re-apply the interior light from <see cref="LightShouldBeOn"/>. Subclasses call this when their state changes.</summary>
		protected void ApplyLight()
		{
			if (_light != null) _light.enabled = LightShouldBeOn;
		}

		private void SnapDoor()
		{
			SnapOne(_door, _doorClosedEuler, _doorOpenEuler);
			if (_extraDoors != null)
				for (int i = 0; i < _extraDoors.Length; i++)
					SnapOne(_extraDoors[i].Door, _extraDoors[i].ClosedEuler, _extraDoors[i].OpenEuler);
		}

		private void SnapOne(Transform door, Vector3 closedEuler, Vector3 openEuler)
		{
			if (door == null) return;
			door.localRotation = Quaternion.Euler(_openLocal ? openEuler : closedEuler);
		}
	}

	/// <summary>One extra swinging door/lid on an <see cref="OpenableAppliance"/>, beyond the primary door — its
	/// own transform + closed/open angles, opened/closed in lockstep with the main door.</summary>
	[System.Serializable]
	public struct ApplianceDoor
	{
		[Tooltip("The door/lid transform that swings.")]
		public Transform Door;
		[Tooltip("Local euler angles when closed.")]
		public Vector3 ClosedEuler;
		[Tooltip("Local euler angles when open.")]
		public Vector3 OpenEuler;
	}
}
