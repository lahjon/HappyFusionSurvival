using System;
using Fusion;
using Starter.Common.Input;
using Starter.Common.Interactions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only orchestrator for "having a crafting bench open". Lives on the Player
	/// prefab next to <see cref="GameInputActions"/> + <see cref="InputContextController"/>.
	/// Mirrors <see cref="Starter.Common.Inventory.LootSession"/>, but the open/close
	/// transition is purely local — the bench is multi-user and stores no per-opener state.
	/// </summary>
	[RequireComponent(typeof(GameInputActions))]
	[RequireComponent(typeof(InputContextController))]
	public sealed class CraftingSession : MonoBehaviour, IInteractionGate
	{
		[Tooltip("Auto-close margin: when the local player walks further than InteractRange * this multiplier from the open bench, the menu closes.")]
		public float AutoCloseRangeMultiplier = 1.5f;

		bool IInteractionGate.AllowInteractions => Current == null;

		public CraftingBench Current { get; private set; }

		/// <summary>True while ANY local CraftingSession has an open bench. Polled by UI gates to suppress menu toggles during crafting.</summary>
		public static bool IsAnyCrafting { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			IsAnyCrafting = false;
		}

		/// <summary>Fires when the open bench changes. Argument is null when closed.</summary>
		public event Action<CraftingBench> OpenedChanged;

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

			if (Current != null) IsAnyCrafting = false;
			Current = null;
		}

		private void Update()
		{
			if (Current == null) return;

			// Auto-close if the player wanders away — the bench has no networked lock,
			// so each opener polices its own range.
			float allowed = Current.InteractRange * AutoCloseRangeMultiplier;
			if ((Current.transform.position - transform.position).sqrMagnitude > allowed * allowed)
			{
				RequestClose();
			}
		}

		/// <summary>Called by <see cref="CraftingBench"/> when the local scanner fires Interact near it.</summary>
		public void TryOpen(CraftingBench bench)
		{
			if (!_initialized || bench == null) return;
			if (Current != null) return;

			Current = bench;
			IsAnyCrafting = true;
			if (_context != null) _context.EnterInventoryMode();
			OpenedChanged?.Invoke(Current);
			Debug.Log($"[CraftingSession] Opened '{bench.DisplayName}'.");
		}

		public void RequestClose()
		{
			if (Current == null) return;

			var closed = Current;
			Current = null;
			IsAnyCrafting = false;
			if (_context != null) _context.EnterPlayerMode();
			OpenedChanged?.Invoke(null);
			Debug.Log($"[CraftingSession] Closed '{closed.DisplayName}'.");
		}

		/// <summary>Forward a craft request to the open bench's state authority. UI should pre-gate via CanCraft.</summary>
		public void RequestCraft(int recipeId)
		{
			if (Current == null || recipeId == 0) return;
			Current.RPC_RequestCraft(recipeId);
		}

		/// <summary>Forward a scavenge request (destroy one unit of an inventory slot → scraps) to the open bench's state authority.</summary>
		public void RequestScavenge(int slotIndex)
		{
			if (Current == null) return;
			Current.RPC_RequestScavenge(slotIndex);
		}

		private void OnClosePerformed(InputAction.CallbackContext ctx)
		{
			RequestClose();
		}
	}
}
