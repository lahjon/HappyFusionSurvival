using System;
using Starter.Common.Input;
using Starter.Common.Interactions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only orchestrator for "having a shopkeeper open". Lives on the Player prefab next to
	/// <see cref="GameInputActions"/> + <see cref="InputContextController"/>. Mirrors
	/// <see cref="QuestSession"/>: the open/close transition is purely local, and the shopkeeper
	/// itself is multi-user — concurrent buyers just race for stock on the state authority.
	/// </summary>
	[RequireComponent(typeof(GameInputActions))]
	[RequireComponent(typeof(InputContextController))]
	public sealed class ShopSession : MonoBehaviour, IInteractionGate
	{
		[Tooltip("Auto-close margin: when the local player walks further than InteractRange * this multiplier from the open shopkeeper, the menu closes.")]
		public float AutoCloseRangeMultiplier = 1.5f;

		bool IInteractionGate.AllowInteractions => Current == null;

		public Shopkeeper Current { get; private set; }

		/// <summary>True while ANY local ShopSession has an open shopkeeper. Polled by UI gates to suppress menu toggles while shopping.</summary>
		public static bool IsAnyOpen { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			IsAnyOpen = false;
		}

		/// <summary>Fires when the open shopkeeper changes. Argument is null when closed.</summary>
		public event Action<Shopkeeper> OpenedChanged;

		private GameInputActions _actions;
		private InputContextController _context;
		private bool _initialized;

		public void Initialize()
		{
			if (_initialized) return;

			_actions = GetComponent<GameInputActions>();
			_context = GetComponent<InputContextController>();

			if (_actions != null && _actions.IsInitialized)
			{
				_actions.Close.performed += OnClosePerformed;
			}

			_initialized = true;
		}

		private void OnDestroy()
		{
			if (!_initialized) return;

			if (_actions != null && _actions.IsInitialized)
			{
				_actions.Close.performed -= OnClosePerformed;
			}

			if (Current != null) IsAnyOpen = false;
			Current = null;
		}

		private void Update()
		{
			if (Current == null) return;

			// Auto-close if the player wanders away — the shopkeeper has no networked lock,
			// so each opener polices its own range.
			float allowed = Current.InteractRange * AutoCloseRangeMultiplier;
			if ((Current.transform.position - transform.position).sqrMagnitude > allowed * allowed)
			{
				RequestClose();
			}
		}

		/// <summary>Called by <see cref="Shopkeeper"/> when the local scanner fires Interact near it.</summary>
		public void TryOpen(Shopkeeper shopkeeper)
		{
			if (!_initialized || shopkeeper == null) return;
			if (Current != null) return;

			Current = shopkeeper;
			IsAnyOpen = true;
			if (_context != null) _context.EnterInventoryMode();
			OpenedChanged?.Invoke(Current);
			Debug.Log($"[ShopSession] Opened '{shopkeeper.DisplayName}'.");
		}

		public void RequestClose()
		{
			if (Current == null) return;

			var closed = Current;
			Current = null;
			IsAnyOpen = false;
			if (_context != null) _context.EnterPlayerMode();
			OpenedChanged?.Invoke(null);
			Debug.Log($"[ShopSession] Closed '{closed.DisplayName}'.");
		}

		/// <summary>Forward a buy request to the open shopkeeper's state authority. UI should pre-gate via stock / money / inventory room.</summary>
		public void RequestBuy(int offerIndex)
		{
			if (Current == null) return;
			Current.RPC_RequestBuy(offerIndex);
		}

		/// <summary>Forward a sell request for the local player's hotbar slot. UI should pre-gate via slot contents / shop sell price.</summary>
		public void RequestSell(int slotIndex)
		{
			if (Current == null) return;
			Current.RPC_RequestSell(slotIndex);
		}

		private void OnClosePerformed(InputAction.CallbackContext ctx)
		{
			RequestClose();
		}
	}
}
