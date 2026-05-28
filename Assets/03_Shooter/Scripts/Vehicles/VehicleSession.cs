using Fusion;
using Starter.Common.Input;
using Starter.Common.Interactions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only session that lives on the player root next to <see cref="GameInputActions"/>
	/// and <see cref="InteractionScanner"/>. While the local player is seated:
	///   - <see cref="IInteractionGate.AllowInteractions"/> returns false, so the scanner
	///     doesn't try to pick up the seat (or any nearby loot) while inside the vehicle.
	///   - The Interact action (E) is intercepted to send an Exit RPC to the player's current
	///     seat, instead of being routed to a new interactable.
	///
	/// Mirrors the <c>LootSession</c> pattern.
	/// </summary>
	[RequireComponent(typeof(GameInputActions))]
	public sealed class VehicleSession : MonoBehaviour, IInteractionGate
	{
		private GameInputActions _actions;
		private Player _player;
		private bool _initialized;

		public bool AllowInteractions => _player == null || _player.InCurrentSeat == null;

		public void Initialize()
		{
			if (_initialized) return;

			_actions = GetComponent<GameInputActions>();
			_player = GetComponent<Player>();

			if (_actions != null && _actions.IsInitialized)
			{
				_actions.Interact.performed += OnInteractPerformed;
			}

			_initialized = true;
		}

		private void OnDestroy()
		{
			if (!_initialized) return;
			if (_actions != null && _actions.IsInitialized)
			{
				_actions.Interact.performed -= OnInteractPerformed;
			}
		}

		private void OnInteractPerformed(InputAction.CallbackContext ctx)
		{
			if (_player == null) return;
			// Another Interact subscriber (the scanner) already handled this press — happens when
			// the scanner just entered us into this seat on the same key press. Without this guard
			// we'd immediately request exit and the player would never seat.
			if (InteractionScanner.InteractConsumedFrame == Time.frameCount) return;

			var seat = _player.InCurrentSeat;
			if (seat == null) return;

			InteractionScanner.InteractConsumedFrame = Time.frameCount;
			seat.RPC_RequestExit();
		}
	}
}
