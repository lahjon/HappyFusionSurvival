using System;
using Starter.Common.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Starter.Common.Interactions
{
	/// <summary>
	/// Local-only scanner that finds the best <see cref="IInteractable"/> in front of
	/// the camera each frame and routes the Interact action to it. Targets self-describe
	/// their range, lock state, and what happens on interaction — so this stays mode-agnostic.
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

		/// <summary>Transform of the currently chosen interactable (convenience accessor used by InteractionPrompt).</summary>
		public static Transform CurrentTarget => CurrentInteractable is Component c ? c.transform : null;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			LocalInstance = null;
			CurrentInteractable = null;
		}

		private GameInputActions _actions;
		private bool _initialized;
		private float _toastSuppressUntil;

		public void Initialize()
		{
			if (_initialized) return;

			_actions = GetComponent<GameInputActions>();

			if (_actions != null && _actions.IsInitialized)
			{
				_actions.Interact.performed += OnInteract;
			}

			LocalInstance = this;
			_initialized = true;
		}

		private void OnDestroy()
		{
			if (!_initialized) return;
			if (_actions != null && _actions.IsInitialized)
			{
				_actions.Interact.performed -= OnInteract;
			}
			if (LocalInstance == this)
			{
				LocalInstance = null;
				CurrentInteractable = null;
			}
		}

		private void Update()
		{
			if (!_initialized) return;

			if (Cursor.lockState != CursorLockMode.Locked || !InteractionsAllowed())
			{
				CurrentInteractable = null;
				return;
			}

			CurrentInteractable = TryFindBest();
		}

		private void OnInteract(InputAction.CallbackContext ctx)
		{
			if (Cursor.lockState != CursorLockMode.Locked) return;
			if (!InteractionsAllowed()) return;

			var best = TryFindBest(includeLocked: true);
			if (best == null) return;

			if (best.CanInteract)
			{
				best.OnInteract(this);
			}
			else if (!string.IsNullOrEmpty(best.LockedReason))
			{
				ShowToast(best.LockedReason);
			}
		}

		/// <summary>
		/// Polled before each scan/interact. Override hook for player-side gates
		/// (e.g. suppress while a UI is open). Returns true by default; mode-specific
		/// gates plug in via <see cref="IInteractionGate"/> on the same GameObject.
		/// </summary>
		private bool InteractionsAllowed()
		{
			var gate = GetComponent<IInteractionGate>();
			return gate == null || gate.AllowInteractions;
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
