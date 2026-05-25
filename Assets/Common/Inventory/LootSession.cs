using System;
using Fusion;
using Starter.Common.Input;
using Starter.Common.Interactions;
using Starter.Common.Inventory.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// Local-only orchestrator for "opening a container". Lives on the Player prefab
	/// next to <see cref="GameInputActions"/> + <see cref="InputContextController"/>.
	/// Tracks the open container, drives input map / cursor toggles, and forwards
	/// drag-drop / right-click / Take-All actions to the container's RPCs.
	/// </summary>
	[RequireComponent(typeof(GameInputActions))]
	[RequireComponent(typeof(InputContextController))]
	public sealed class LootSession : MonoBehaviour, IInteractionGate
	{
		bool IInteractionGate.AllowInteractions => Current == null;

		public LootContainer Current { get; private set; }

		/// <summary>True while ANY local LootSession has an open container. Polled by UIGameMenu to suppress menu toggle / cursor lock during looting.</summary>
		public static bool IsAnyLooting { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			IsAnyLooting = false;
		}

		/// <summary>Fires when the open container changes. Argument is null when closed.</summary>
		public event Action<LootContainer> OpenedChanged;

		private GameInputActions _actions;
		private InputContextController _context;
		private NetworkRunner _runner;
		private PlayerRef _self;
		private bool _initialized;
		private LootContainer _tracked;

		public void Initialize(NetworkRunner runner, PlayerRef self)
		{
			if (_initialized) return;

			_runner = runner;
			_self = self;
			_actions = GetComponent<GameInputActions>();
			_context = GetComponent<InputContextController>();

			if (_actions != null && _actions.IsInitialized)
			{
				_actions.Close.performed += OnClosePerformed;
				_actions.TakeAll.performed += OnTakeAllPerformed;
			}

			BuildLootUI();

			_initialized = true;
		}

		private void BuildLootUI()
		{
			var playerInv = GetComponent<IPlayerInventory>();
			if (playerInv == null) return;

			var uiGO = new GameObject("LootUIRoot");
			uiGO.transform.SetParent(transform, false);
			var ui = uiGO.AddComponent<LootUI>();
			ui.Bind(this, playerInv);
		}

		private void OnDestroy()
		{
			if (!_initialized) return;

			if (_actions != null && _actions.IsInitialized)
			{
				_actions.Close.performed -= OnClosePerformed;
				_actions.TakeAll.performed -= OnTakeAllPerformed;
			}

			if (Current != null) IsAnyLooting = false;
			Untrack();
		}

		/// <summary>Called by the player-side scanner when the Interact action fires near an unlocked container.</summary>
		public void TryOpen(LootContainer container)
		{
			if (!_initialized || container == null) return;
			if (Current != null) return;
			if (container.CurrentUser != PlayerRef.None) return;

			Track(container);
			container.RPC_RequestOpen();
			Debug.Log($"[LootSession] TryOpen -> {container.DisplayName}");
		}

		public void RequestMove(byte fromInv, byte fromSlot, byte toInv, byte toSlot)
		{
			if (Current == null) return;
			Current.RPC_RequestMove(fromInv, fromSlot, toInv, toSlot);
		}

		public void RequestSwap(byte fromInv, byte fromSlot)
		{
			if (Current == null) return;
			Current.RPC_RequestSwapToOther(fromInv, fromSlot);
		}

		public void RequestTakeAll()
		{
			if (Current == null) return;
			Current.RPC_RequestTakeAll();
		}

		public void RequestClose()
		{
			if (Current == null) return;
			Current.RPC_RequestClose();
		}

		private void Track(LootContainer next)
		{
			if (ReferenceEquals(_tracked, next)) return;
			Untrack();
			_tracked = next;
			if (_tracked != null)
			{
				_tracked.UserChanged += OnContainerUserChanged;
			}
		}

		private void Untrack()
		{
			if (_tracked != null)
			{
				_tracked.UserChanged -= OnContainerUserChanged;
				_tracked = null;
			}
		}

		private void OnContainerUserChanged(LootContainer source, PlayerRef previous, PlayerRef current)
		{
			if (source != _tracked) return;

			if (current == _self && Current == null)
			{
				Current = source;
				IsAnyLooting = true;
				if (_context != null) _context.EnterInventoryMode();
				OpenedChanged?.Invoke(Current);
				Debug.Log($"[LootSession] Opened '{Current.DisplayName}' as {_self}.");
			}
			else if (current == PlayerRef.None && previous == _self && source == Current)
			{
				var closed = Current;
				Current = null;
				IsAnyLooting = false;
				Untrack();
				if (_context != null) _context.EnterPlayerMode();
				OpenedChanged?.Invoke(null);
				Debug.Log($"[LootSession] Closed '{closed.DisplayName}'.");
			}
			else if (current != PlayerRef.None && current != _self && Current == null)
			{
				// Race lost on the container we were trying to open.
				Untrack();
			}
		}

		private void OnClosePerformed(InputAction.CallbackContext ctx)
		{
			RequestClose();
		}

		private void OnTakeAllPerformed(InputAction.CallbackContext ctx)
		{
			RequestTakeAll();
		}
	}
}
