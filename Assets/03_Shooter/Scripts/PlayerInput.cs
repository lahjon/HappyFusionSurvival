using UnityEngine;
using Fusion;
using Starter.Common.Input;
using Starter.Common.Interactions;
using Starter.Common.Inventory;

namespace Starter.Shooter
{
	public enum EInputButton
	{
		Jump,
		Fire,
		Sprint,
	}

	/// <summary>
	/// Input structure sent over network to the server.
	/// </summary>
	public struct GameplayInput : INetworkInput
	{
		public Vector2 LookRotation;
		public Vector2 MoveDirection;
		public NetworkButtons Buttons;
	}

	/// <summary>
	/// PlayerInput handles accumulating player input from Unity and passes the accumulated input to Fusion.
	/// This version of PlayerInput showcases usage of IBeforeUpdate and IAfterTick callbacks.
	/// </summary>
	[RequireComponent(typeof(GameInputActions))]
	public sealed class PlayerInput : NetworkBehaviour, IBeforeUpdate, IAfterTick
	{
		[Range(0.01f, 1f)]
		public float LookSensitivity = 0.1f;

		[Networked]
		public NetworkButtons PreviousButtons { get; private set; }
		public Vector2 LookRotation => _input.LookRotation;

		private GameplayInput _input;
		private GameInputActions _actions;

		public override void Spawned()
		{
			if (HasInputAuthority == false)
				return;

			_actions = GetComponent<GameInputActions>();
			if (_actions != null)
			{
				_actions.EnableForLocalPlayer();
			}

			var lootSession = GetComponent<LootSession>();
			if (lootSession != null)
			{
				lootSession.Initialize(Runner, Object.InputAuthority);
			}

			var scanner = GetComponent<InteractionScanner>();
			if (scanner != null)
			{
				scanner.Initialize();
			}

			// Register to Fusion input poll callback
			var networkEvents = Runner.GetComponent<NetworkEvents>();
			networkEvents.OnInput.AddListener(OnInput);
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (runner == null)
				return;

			var networkEvents = runner.GetComponent<NetworkEvents>();
			if (networkEvents != null)
			{
				networkEvents.OnInput.RemoveListener(OnInput);
			}
		}

		// BeforeUpdate is called during Unity's Update loop before any OnInput/FixedUpdateNetwork/Render functions are executed.
		// Therefore using BeforeUpdate to accumulate input is slightly more precise than doing so in Update function as the latest input
		// will be already used in FixedUpdateNetwork if it will be called in this update loop. This gets more important the lower render rate the player has.
		void IBeforeUpdate.BeforeUpdate()
		{
			// Accumulate input from Keyboard/Mouse. Input accumulation is mandatory (at least for the look rotation) as Update can be
			// called multiple times before next OnInput is called - common if rendering speed is faster than Fusion simulation.

			if (HasInputAuthority == false)
				return;

			if (_actions == null || _actions.IsInitialized == false)
				return;

			// Accumulate input only if the cursor is locked.
			if (Cursor.lockState != CursorLockMode.Locked)
			{
				_input.MoveDirection = default;
				return;
			}

			var look = _actions.Look.ReadValue<Vector2>();
			_input.LookRotation += new Vector2(-look.y, look.x) * LookSensitivity;

			var moveDirection = _actions.Move.ReadValue<Vector2>();
			_input.MoveDirection = moveDirection.normalized;

			_input.Buttons.Set(EInputButton.Fire, _actions.Fire.IsPressed());
			_input.Buttons.Set(EInputButton.Jump, _actions.Jump.IsPressed());
			_input.Buttons.Set(EInputButton.Sprint, _actions.Sprint.IsPressed());
		}

		// AfterTick is called after all FixedUpdateNetwork calls on NetworkBehaviours were executed for this tick.
		// It is perfect for actions that should be executed at the end of the tick.
		void IAfterTick.AfterTick()
		{
			// Save current button input (if any) as previous.
			// Previous buttons need to be networked to detect correctly pressed/released events.
			if (GetInput(out GameplayInput input))
			{
				PreviousButtons = input.Buttons;
			}
		}

		// Fusion polls accumulated input. This callback can be executed multiple times in a row if there is a performance spike.
		private void OnInput(NetworkRunner runner, NetworkInput networkInput)
		{
			networkInput.Set(_input);
		}
	}
}
