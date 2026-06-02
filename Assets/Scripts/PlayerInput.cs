using UnityEngine;
using Fusion;
using QFSW.QC;
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
		ClimbDrop,
		Crouch,
		Brake,
		SecondaryFire,
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

		/// <summary>Snap the locally accumulated look rotation (x = pitch, y = yaw) so the next ticks aim there. Input authority only — call e.g. when entering a vehicle to face the cab forward.</summary>
		public void SetLookRotation(Vector2 lookRotation)
		{
			if (HasInputAuthority == false) return;
			_input.LookRotation = lookRotation;
		}

		private GameplayInput _input;
		private GameInputActions _actions;
		private Player _player;
		private Inventory _inventory;
		private InputContextController _inputContext;
		private QuantumConsole _console;

		// Aim recoil/sway state — local to input authority. The effect is baked into
		// _input.LookRotation each frame, which is networked, so proxies see the same
		// swayed/recoiled aim. We track only the per-frame delta so values don't
		// double-accumulate into LookRotation.
		private Vector2 _recoilPending;
		private Vector2 _recoilApplied;
		private Vector2 _prevSwayOffset;

		public override void Spawned()
		{
			if (HasInputAuthority == false)
				return;

			_player = GetComponent<Player>();
			_inventory = GetComponent<Inventory>();
			_actions = GetComponent<GameInputActions>();
			if (_actions != null)
			{
				_actions.EnableForLocalPlayer();
			}

			// While the Quantum Console is open it owns all input — disable the gameplay/UI action
			// maps so nothing leaks through while typing. Escape-to-close stays alive via MenuManager.
			_inputContext = GetComponent<InputContextController>();
			_console = ResolveConsole();
			if (_console != null)
			{
				_console.OnActivate += OnConsoleActivated;
				_console.OnDeactivate += OnConsoleDeactivated;

				// Sync up in case the console was already open when we spawned in.
				if (_console.IsActive)
					_inputContext?.SetConsoleSuppressed(true);
			}

			var lootSession = GetComponent<LootSession>();
			if (lootSession != null)
			{
				lootSession.Initialize(Runner, Object.InputAuthority);
			}

			var craftingSession = GetComponent<CraftingSession>();
			if (craftingSession != null)
			{
				craftingSession.Initialize();
			}

			var questSession = GetComponent<QuestSession>();
			if (questSession != null)
			{
				questSession.Initialize();
			}

			var shopSession = GetComponent<ShopSession>();
			if (shopSession != null)
			{
				shopSession.Initialize();
			}

			var computerSession = GetComponent<ComputerSession>();
			if (computerSession != null)
			{
				computerSession.Initialize();
			}

			var sleepSession = GetComponent<SleepSession>();
			if (sleepSession != null)
			{
				sleepSession.Initialize();
			}

			var scanner = GetComponent<InteractionScanner>();
			if (scanner != null)
			{
				scanner.Initialize();
			}

			var vehicleSession = GetComponent<VehicleSession>();
			if (vehicleSession != null)
			{
				vehicleSession.Initialize();
			}

			// Register to Fusion input poll callback
			var networkEvents = Runner.GetComponent<NetworkEvents>();
			networkEvents.OnInput.AddListener(OnInput);
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (_console != null)
			{
				_console.OnActivate -= OnConsoleActivated;
				_console.OnDeactivate -= OnConsoleDeactivated;
				_console = null;
			}

			if (runner == null)
				return;

			var networkEvents = runner.GetComponent<NetworkEvents>();
			if (networkEvents != null)
			{
				networkEvents.OnInput.RemoveListener(OnInput);
			}
		}

		private void OnConsoleActivated() => _inputContext?.SetConsoleSuppressed(true);
		private void OnConsoleDeactivated() => _inputContext?.SetConsoleSuppressed(false);

		// QuantumConsole.Instance is only set when its singleton option is enabled; fall back to a scene lookup.
		private static QuantumConsole ResolveConsole()
		{
			return QuantumConsole.Instance != null
				? QuantumConsole.Instance
				: FindAnyObjectByType<QuantumConsole>(FindObjectsInactive.Include);
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

			bool isSeated = _player != null && _player.IsSeated;
			bool isDowned = _player != null && _player.IsDowned;

			// While knocked out or getting up, freeze input so the player can't move, fire,
			// or steer the look direction. Look angle stays where it was so the camera doesn't
			// snap when control returns.
			//
			// Seated state is also "input-locked" for normal player actions (no fire, jump,
			// sprint, climb), but Look still flows so the head can turn, and MoveDirection / Fire
			// flow through unchanged so the Vehicle can read driver steering + honk.
			//
			// Downed is a partial lock: WASD + Look flow through so the player can crawl and
			// look around, but action Buttons (fire/jump/sprint/crouch) stay clear. Player.cs
			// reads MoveDirection in ProcessDownedTick and applies DownedCrawlSpeed.
			if (_player != null && _player.IsInputLocked && isSeated == false && isDowned == false)
			{
				_input.MoveDirection = default;
				_input.Buttons = default;
				return;
			}

			var look = _actions.Look.ReadValue<Vector2>();
			_input.LookRotation += new Vector2(-look.y, look.x) * LookSensitivity;

			// While climbing the player can't fire/sprint and weapon sway/recoil would visibly twitch
			// the camera against the wall. Suppress both so the climb experience stays clean.
			bool isClimbing = _player != null && _player.IsClimbing;

			if (isClimbing == false && isSeated == false && isDowned == false)
			{
				ApplyWeaponSwayAndRecoil();
			}

			if (isDowned)
			{
				// Crawl path: WASD + Look only. Action buttons must stay clear so a fire/jump/sprint
				// hold doesn't leak into the next tick when the player gets revived.
				var moveDirectionDowned = _actions.Move.ReadValue<Vector2>();
				_input.MoveDirection = moveDirectionDowned.normalized;
				_input.Buttons = default;
				return;
			}

			if (isSeated)
			{
				// Driver: WASD goes to vehicle as throttle/steer, Fire is honk, Space (Jump action)
				// doubles as the brake/handbrake while seated. Passenger sends the same fields but
				// the vehicle ignores them (input authority is on the driver only).
				var moveSeated = _actions.Move.ReadValue<Vector2>();
				_input.MoveDirection = moveSeated.normalized;
				_input.Buttons = default;
				_input.Buttons.Set(EInputButton.Fire, _actions.Fire.IsPressed());
				_input.Buttons.Set(EInputButton.Brake, _actions.Jump.IsPressed());
				_input.Buttons.Set(EInputButton.Sprint, _actions.Sprint.IsPressed());
			}
			else
			{
				var moveDirection = _actions.Move.ReadValue<Vector2>();
				_input.MoveDirection = moveDirection.normalized;

				bool crouchHeld = _actions.Crouch.IsPressed();
				_input.Buttons.Set(EInputButton.Fire, !isClimbing && _actions.Fire.IsPressed());
				_input.Buttons.Set(EInputButton.SecondaryFire, !isClimbing && _actions.AltAttack.IsPressed());
				_input.Buttons.Set(EInputButton.Jump, _actions.Jump.IsPressed());
				_input.Buttons.Set(EInputButton.Sprint, !isClimbing && _actions.Sprint.IsPressed());
				// Same physical key drives both. While anchored on a wall it means "let go",
				// otherwise "crouch". ProcessClimbInput / ProcessInput each read only the relevant button.
				_input.Buttons.Set(EInputButton.ClimbDrop, crouchHeld);
				_input.Buttons.Set(EInputButton.Crouch, !isClimbing && crouchHeld);
			}
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

		// Applies the held ranged weapon's sway and CS-style aim recoil on top of mouse look.
		// Both are written as per-frame deltas into _input.LookRotation so they ride along the
		// existing input pipeline (clamped + networked by the receiving Player).
		private void ApplyWeaponSwayAndRecoil()
		{
			var weapon = GetRangedWeapon();

			// While aiming (ADS), steady the sway and flatten the recoil per the weapon's AimTuning.
			bool aiming = _player != null && _player.IsAiming;
			var aim = aiming && _inventory != null ? _inventory.ActiveWeapon?.Aim : null;
			float swayMul = aim != null ? aim.SwayMultiplier : 1f;
			float recoilMul = aim != null ? aim.RecoilMultiplier : 1f;

			// Recoil edge-trigger: each press queues a fresh kick toward _recoilPending.
			if (weapon != null && weapon.AimRecoil > 0f && _actions.Fire.WasPressedThisFrame())
			{
				float r = weapon.AimRecoil * recoilMul;
				// LookRotation.x = pitch; more negative = look up. So pitch impulse is negative.
				float pitchImpulse = -weapon.AimRecoilPitchPerShot * r;
				float yawImpulse = Random.Range(-weapon.AimRecoilHorizontalRandom, weapon.AimRecoilHorizontalRandom) * r;
				_recoilPending += new Vector2(pitchImpulse, yawImpulse);
			}

			float lerpSpeed = weapon != null ? Mathf.Max(0f, weapon.AimRecoilLerpSpeed) : 18f;
			Vector2 newApplied = Vector2.Lerp(_recoilApplied, _recoilPending, 1f - Mathf.Exp(-lerpSpeed * Time.deltaTime));
			Vector2 recoilDelta = newApplied - _recoilApplied;
			_recoilApplied = newApplied;

			// Sway: Perlin wobble around zero. Apply only the delta vs. previous frame so the
			// oscillation doesn't bake into the running LookRotation total.
			Vector2 newSway = Vector2.zero;
			if (weapon != null && weapon.WeaponSway > 0f)
			{
				float t = Time.time * Mathf.Max(0.0001f, weapon.SwayFrequency);
				float amp = weapon.WeaponSway * weapon.SwayMaxDegrees * swayMul;
				newSway.x = (Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f * amp;
				newSway.y = (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f * amp;
			}
			Vector2 swayDelta = newSway - _prevSwayOffset;
			_prevSwayOffset = newSway;

			_input.LookRotation += recoilDelta + swayDelta;
		}

		private HeldWeapon GetRangedWeapon()
		{
			if (_inventory == null) return null;
			var instance = _inventory.HeldInstance;
			if (instance == null) return null;
			var weapon = instance.GetComponent<HeldWeapon>();
			if (weapon == null) return null;
			// Sway / aim-recoil apply only when the active action is a ranged firearm. The action
			// now lives on the item asset (Inventory.ActiveAction), not the HeldWeapon prefab.
			var action = _inventory.ActiveAction;
			if (action == null || action.Style != EFeedbackStyle.Ranged) return null;
			return weapon;
		}
	}
}
