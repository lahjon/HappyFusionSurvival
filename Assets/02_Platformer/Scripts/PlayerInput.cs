using UnityEngine;
using Fusion;

namespace Starter.Platformer
{
	public enum EInputButton
	{
		Jump,
		Sprint,
	}

	/// <summary>
	/// Input structure sent over network to the server.
	/// </summary>
	public struct GameplayInput : INetworkInput
	{
		public Vector2 MoveDirection;
		public Vector2 LookRotation;
		public NetworkButtons Buttons;
	}

	/// <summary>
	/// PlayerInput handles accumulating player input from Unity and passes the accumulated input to Fusion.
	/// </summary>
	public sealed class PlayerInput : NetworkBehaviour
	{
		public float InitialLookRotation = 18f;

		public Vector2 LookRotation => _input.LookRotation;

		private GameplayInput _input;

		public override void Spawned()
		{
			if (HasInputAuthority == false)
				return;

			// Register to Fusion input poll callback
			var networkEvents = Runner.GetComponent<NetworkEvents>();
			networkEvents.OnInput.AddListener(OnInput);

			_input.LookRotation.x = InitialLookRotation;
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

		private void Update()
		{
			// Accumulate input from Keyboard/Mouse. Input accumulation is mandatory (at least for look rotation) as Update can be
			// called multiple times before next OnInput is called - common if rendering speed is faster than Fusion simulation.

			if (HasInputAuthority == false)
				return;

			// Accumulate input only if the cursor is locked.
			if (Cursor.lockState != CursorLockMode.Locked)
			{
				_input.MoveDirection = default;
				return;
			}

			var lookRotationDelta = new Vector2(-Input.GetAxisRaw("Mouse Y"), Input.GetAxisRaw("Mouse X"));
			_input.LookRotation = ClampLookRotation(_input.LookRotation + lookRotationDelta);

			var moveDirection = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
			_input.MoveDirection = moveDirection.normalized;

			_input.Buttons.Set(EInputButton.Jump, Input.GetButton("Jump"));
			_input.Buttons.Set(EInputButton.Sprint, Input.GetButton("Sprint"));
		}

		// Fusion polls accumulated input. This callback can be executed multiple times in a row if there is a performance spike.
		private void OnInput(NetworkRunner runner, NetworkInput networkInput)
		{
			networkInput.Set(_input);
		}

		private Vector2 ClampLookRotation(Vector2 lookRotation)
		{
			lookRotation.x = Mathf.Clamp(lookRotation.x, -30f, 70f);
			return lookRotation;
		}
	}
}
