using System;
using System.Collections.Generic;
using Starter.Common.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Starter.Common.Interactions
{
	/// <summary>
	/// Local-only scanner that finds the best <see cref="IInteractable"/> in front of
	/// the camera each frame and routes the Interact action to it. Targets self-describe
	/// their range, lock state, and what happens on interaction — so this stays mode-agnostic.
	///
	/// Two paths: tap Interact → <see cref="IInteractable.OnInteract"/>; hold Interact
	/// (for targets that also implement <see cref="IPickupableStation"/>) → pickup RPC.
	/// </summary>
	[RequireComponent(typeof(GameInputActions))]
	public sealed class InteractionScanner : MonoBehaviour
	{
		[Tooltip("Maximum radius scanned for interactables. Per-target InteractRange is still enforced; use this as an upper bound.")]
		public float ScanRadius = 5f;

		[Tooltip("Minimum dot(cameraForward, toTarget). 0 = anywhere in the front hemisphere; 0.5 ≈ within a 60° cone; 1 = exactly looking at it.")]
		[Range(-1f, 1f)]
		public float ViewConeDot = 0.3f;

		[Tooltip("Toast cooldown so locked-prompt messages don't spam.")]
		public float ToastCooldown = 1f;

		public event Action<string> Toast;

		/// <summary>Local-only singleton; set when the local player's scanner initializes.</summary>
		public static InteractionScanner LocalInstance { get; private set; }

		/// <summary>The component the local scanner has currently chosen as the interactable in focus (null when nothing in range/in cone). Read by InteractionPrompt to highlight itself.</summary>
		public static IInteractable CurrentInteractable { get; private set; }

		/// <summary>Transform of the currently chosen interactable (convenience accessor used by InteractionPrompt).
		/// Uses Unity-overloaded null-check so a target destroyed mid-frame (e.g. Runner.Despawn) returns null
		/// instead of throwing MissingReferenceException.</summary>
		public static Transform CurrentTarget
		{
			get
			{
				var c = CurrentInteractable as Component;
				return c != null ? c.transform : null;
			}
		}

		/// <summary>
		/// Transform whose pickup hold is currently in progress on the local client.
		/// InteractionPrompt reads this + <see cref="HoldProgress"/> to draw the radial fill.
		/// </summary>
		public static Transform HoldingTarget { get; private set; }

		/// <summary>0..1 fraction of the active hold timer. 0 when no hold is in progress.</summary>
		public static float HoldProgress { get; private set; }

		/// <summary>
		/// True when the local scanner is actively scanning this frame — i.e. cursor is locked AND
		/// no <see cref="IInteractionGate"/> is vetoing. False while a UI panel is open, the player
		/// is seated in a vehicle, etc. <see cref="InteractionPrompt"/> reads this to hide all
		/// world-space prompts during those states.
		/// </summary>
		public static bool IsScanningActive { get; private set; }

		/// <summary>
		/// Frame number when something already handled the Interact press this frame.
		/// Used to prevent multiple Interact subscribers (scanner + VehicleSession) from
		/// both acting on the same key press — first one to fire wins, the rest see this
		/// flag and skip.
		/// </summary>
		public static int InteractConsumedFrame = -1;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			LocalInstance = null;
			CurrentInteractable = null;
			HoldingTarget = null;
			HoldProgress = 0f;
			IsScanningActive = false;
			InteractConsumedFrame = -1;
		}

		private GameInputActions _actions;
		private bool _initialized;
		private float _toastSuppressUntil;
		private readonly List<IInteractionGate> _gates = new List<IInteractionGate>();

		// Hold-to-pickup state. Tap path stays simple: fire OnInteract immediately on
		// release for non-pickupable targets. For pickupable targets we wait until
		// release to decide tap-vs-hold so the same key drives both.
		private IInteractable _pressTarget;
		private IPickupableStation _pressPickup;
		private float _pressStartTime;
		private bool _pickupFired;

		public void Initialize()
		{
			if (_initialized) return;

			_actions = GetComponent<GameInputActions>();

			if (_actions != null && _actions.IsInitialized)
			{
				_actions.Interact.performed += OnInteractPressed;
				_actions.Interact.canceled += OnInteractReleased;
			}

			LocalInstance = this;
			_initialized = true;
		}

		private void OnDestroy()
		{
			if (!_initialized) return;
			if (_actions != null && _actions.IsInitialized)
			{
				_actions.Interact.performed -= OnInteractPressed;
				_actions.Interact.canceled -= OnInteractReleased;
			}
			if (LocalInstance == this)
			{
				LocalInstance = null;
				CurrentInteractable = null;
				HoldingTarget = null;
				HoldProgress = 0f;
				IsScanningActive = false;
			}
		}

		private void Update()
		{
			if (!_initialized) return;

			bool active = Cursor.lockState == CursorLockMode.Locked && InteractionsAllowed();
			IsScanningActive = active;

			if (!active)
			{
				CurrentInteractable = null;
				AbortHold();
				return;
			}

			CurrentInteractable = TryFindBest();

			TickHold();
		}

		private void TickHold()
		{
			if (_pressTarget == null || _pressPickup == null) return;
			if (_pickupFired) return;

			// Cancel hold if the player looked away from the target (or it disappeared).
			if (CurrentInteractable != _pressTarget)
			{
				AbortHold();
				return;
			}

			// Cancel if the target became blocked mid-hold (e.g. someone else opened the crate).
			string blocked = _pressPickup.PickupBlockedReason;
			if (!string.IsNullOrEmpty(blocked))
			{
				AbortHold();
				return;
			}

			float duration = Mathf.Max(0.01f, _pressPickup.PickupHoldSeconds);
			float elapsed = Time.unscaledTime - _pressStartTime;
			HoldingTarget = (_pressTarget is Component c) ? c.transform : null;
			HoldProgress = Mathf.Clamp01(elapsed / duration);

			if (elapsed >= duration)
			{
				_pickupFired = true;
				InteractConsumedFrame = Time.frameCount;
				_pressPickup.LocalRequestPickup();
				// The RPC despawns the target — drop our reference so consumers (prompt UI,
				// other scanner reads) can't dereference a destroyed Component this frame.
				CurrentInteractable = null;
				HoldingTarget = null;
				HoldProgress = 0f;
			}
		}

		private void OnInteractPressed(InputAction.CallbackContext ctx)
		{
			if (Cursor.lockState != CursorLockMode.Locked) return;
			if (!InteractionsAllowed()) return;
			if (InteractConsumedFrame == Time.frameCount) return;

			var best = TryFindBest(includeLocked: true);
			if (best == null) return;

			// Pickupable targets defer the tap decision to release; non-pickupable fire
			// the tap immediately on press (instant feedback, matches old behavior).
			var pickup = best as IPickupableStation;
			if (pickup != null && pickup.IsPickupable)
			{
				_pressTarget = best;
				_pressPickup = pickup;
				_pressStartTime = Time.unscaledTime;
				_pickupFired = false;
				HoldingTarget = (best is Component c) ? c.transform : null;
				HoldProgress = 0f;

				string blocked = pickup.PickupBlockedReason;
				if (!string.IsNullOrEmpty(blocked))
				{
					// Don't engage the hold path while blocked — toast and let the tap-on-release
					// path still open the UI (e.g. show "Empty first" but still let them open the crate).
					ShowToast(blocked);
				}
				return;
			}

			FireTap(best);
		}

		private void OnInteractReleased(InputAction.CallbackContext ctx)
		{
			// On release, if we engaged the hold path but never finished it, treat as a tap.
			if (_pressTarget != null && !_pickupFired)
			{
				// Only fire if the player is still looking at the same target and it's allowed.
				if (CurrentInteractable == _pressTarget && InteractConsumedFrame != Time.frameCount)
				{
					FireTap(_pressTarget);
				}
			}
			AbortHold();
		}

		private void FireTap(IInteractable target)
		{
			if (target.CanInteract)
			{
				InteractConsumedFrame = Time.frameCount;
				target.OnInteract(this);
			}
			else if (!string.IsNullOrEmpty(target.LockedReason))
			{
				InteractConsumedFrame = Time.frameCount;
				ShowToast(target.LockedReason);
			}
		}

		private void AbortHold()
		{
			_pressTarget = null;
			_pressPickup = null;
			_pressStartTime = 0f;
			_pickupFired = false;
			HoldingTarget = null;
			HoldProgress = 0f;
		}

		/// <summary>
		/// Polled before each scan/interact. Returns false if ANY <see cref="IInteractionGate"/>
		/// sibling on the player root vetoes (e.g. an inventory UI is open, or the player is
		/// already seated). All gates are ANDed together — important now that the player has
		/// multiple gate sources (LootSession, CraftingSession, ComputerSession, VehicleSession).
		/// </summary>
		private bool InteractionsAllowed()
		{
			_gates.Clear();
			GetComponents(_gates);
			for (int i = 0; i < _gates.Count; i++)
			{
				if (!_gates[i].AllowInteractions) return false;
			}
			return true;
		}

		private IInteractable TryFindBest(bool includeLocked = false)
		{
			var camT = Camera.main != null ? Camera.main.transform : transform;
			Vector3 camPos = camT.position;
			Vector3 camFwd = camT.forward;
			Vector3 playerPos = transform.position;

			IInteractable best = null;
			float bestScore = ViewConeDot;

			var hits = Physics.OverlapSphere(playerPos, ScanRadius);
			for (int i = 0; i < hits.Length; i++)
			{
				var col = hits[i];
				if (col == null) continue;

				var candidate = col.GetComponentInParent<IInteractable>();
				if (candidate == null) continue;
				if (!includeLocked && !candidate.CanInteract) continue;

				Vector3 point = candidate.InteractionPoint;
				float range = candidate.InteractRange;
				if ((point - playerPos).sqrMagnitude > range * range) continue;

				float score = LookAlignment(point, camPos, camFwd);
				if (score <= bestScore) continue;

				bestScore = score;
				best = candidate;
			}

			return best;
		}

		private static float LookAlignment(Vector3 targetPos, Vector3 camPos, Vector3 camFwd)
		{
			var to = targetPos - camPos;
			float lenSq = to.sqrMagnitude;
			if (lenSq < 0.0001f) return 1f;
			return Vector3.Dot(camFwd, to) / Mathf.Sqrt(lenSq);
		}

		private void ShowToast(string text)
		{
			if (Time.unscaledTime < _toastSuppressUntil) return;
			_toastSuppressUntil = Time.unscaledTime + ToastCooldown;
			Debug.Log($"[InteractionScanner] {text}");
			Toast?.Invoke(text);
		}
	}

	/// <summary>
	/// Optional sibling on the player root that gates whether the scanner is allowed
	/// to operate this frame. Used by <c>LootSession</c> to suppress interactions
	/// while a loot UI is open without coupling the scanner to inventory code.
	/// </summary>
	public interface IInteractionGate
	{
		bool AllowInteractions { get; }
	}
}
