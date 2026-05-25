using System;
using Fusion;
using Starter.Common.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// Local-only scanner that finds the nearest <see cref="PickupableItem"/> or
	/// <see cref="LootContainer"/> each Update and routes the Interact action to
	/// the appropriate handler. The mode-specific player inventory implements
	/// <see cref="IInteractionTarget"/> so the scanner stays mode-agnostic.
	/// </summary>
	[RequireComponent(typeof(GameInputActions))]
	[RequireComponent(typeof(LootSession))]
	public sealed class InteractionScanner : MonoBehaviour
	{
		[Tooltip("Radius used when scanning for nearby pickups / containers. Per-target ranges are still enforced.")]
		public float ScanRadius = 3f;

		[Tooltip("Fallback pickup range used if no IInteractionTarget is attached.")]
		public float DefaultPickupRange = 2f;

		public event Action<string> Toast;

		private GameInputActions _actions;
		private LootSession _loot;
		private IInteractionTarget _target;
		private bool _initialized;
		private float _toastSuppressUntil;

		public void Initialize()
		{
			if (_initialized) return;

			_actions = GetComponent<GameInputActions>();
			_loot = GetComponent<LootSession>();
			_target = GetComponent<IInteractionTarget>();

			if (_actions != null && _actions.IsInitialized)
			{
				_actions.Interact.performed += OnInteract;
			}

			_initialized = true;
		}

		private void OnDestroy()
		{
			if (!_initialized) return;
			if (_actions != null && _actions.IsInitialized)
			{
				_actions.Interact.performed -= OnInteract;
			}
		}

		private void OnInteract(InputAction.CallbackContext ctx)
		{
			if (Cursor.lockState != CursorLockMode.Locked)
			{
				Debug.Log("[InteractionScanner] E ignored: cursor not locked.");
				return;
			}
			if (_loot != null && _loot.Current != null)
			{
				Debug.Log("[InteractionScanner] E ignored: already looting.");
				return;
			}

			if (TryFindBest(out var pickup, out var container))
			{
				if (pickup != null)
				{
					Debug.Log($"[InteractionScanner] E -> pickup {pickup.name}");
					_target?.OnPickupRequested(pickup);
					return;
				}

				if (container != null)
				{
					if (container.CurrentUser == PlayerRef.None)
					{
						Debug.Log($"[InteractionScanner] E -> open {container.DisplayName}");
						_loot.TryOpen(container);
					}
					else
					{
						ShowToast($"{container.DisplayName} is in use");
					}
				}
			}
			else
			{
				Debug.Log("[InteractionScanner] E pressed, nothing in range.");
			}
		}

		private bool TryFindBest(out PickupableItem pickup, out LootContainer container)
		{
			pickup = null;
			container = null;

			float bestPickupSq = (_target != null ? _target.PickupRange : DefaultPickupRange);
			bestPickupSq *= bestPickupSq;
			float bestContainerSq = float.PositiveInfinity;

			var hits = Physics.OverlapSphere(transform.position, ScanRadius);
			for (int i = 0; i < hits.Length; i++)
			{
				var col = hits[i];
				if (col.TryGetComponent<PickupableItem>(out var pi))
				{
					float dsq = (pi.transform.position - transform.position).sqrMagnitude;
					if (dsq <= bestPickupSq)
					{
						bestPickupSq = dsq;
						pickup = pi;
					}
					continue;
				}

				if (col.TryGetComponent<LootContainer>(out var lc))
				{
					float range = lc.InteractRange;
					float dsq = (lc.transform.position - transform.position).sqrMagnitude;
					if (dsq <= range * range && dsq < bestContainerSq)
					{
						bestContainerSq = dsq;
						container = lc;
					}
				}
			}

			// Pickup takes priority — they're typically smaller and on top of containers.
			if (pickup != null) container = null;
			return pickup != null || container != null;
		}

		private void ShowToast(string text)
		{
			if (Time.unscaledTime < _toastSuppressUntil) return;
			_toastSuppressUntil = Time.unscaledTime + 1.0f;
			Debug.Log($"[InteractionScanner] {text}");
			Toast?.Invoke(text);
		}
	}

	/// <summary>
	/// Implemented by the mode-specific player inventory (e.g. Starter.Shooter.Inventory)
	/// so the shared scanner can hand off pickup requests without taking a hard dependency.
	/// </summary>
	public interface IInteractionTarget
	{
		float PickupRange { get; }
		void OnPickupRequested(PickupableItem pickup);
	}
}
