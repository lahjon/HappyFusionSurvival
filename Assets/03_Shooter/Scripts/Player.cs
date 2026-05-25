using UnityEngine;
using Fusion;
using Fusion.Addons.SimpleKCC;
using UnityEngine.Rendering;

namespace Starter.Shooter
{
	public enum ERagdollState : byte
	{
		Normal = 0,
		KnockedOut = 1,
		GettingUp = 2,
		Dead = 3,
	}

	/// <summary>
	/// Main player scrip - controls player movement and animations.
	/// </summary>
	public sealed class Player : NetworkBehaviour, IKnockbackable
	{
		[Header("References")]
		public Health Health;
		public SimpleKCC KCC;
		public PlayerInput Input;
		public Animator Animator;
		public Transform CameraPivot;
		public Transform CameraHandle;
		public Transform ScalingRoot;
		public UINameplate Nameplate;
		public HitboxRoot HitboxRoot;
		public Renderer[] HeadRenderers;

		[Header("Movement Setup")]
		public float WalkSpeed = 2f;
		public float SprintSpeed = 8f;
		public float JumpImpulse = 10f;
		public float UpGravity = 25f;
		public float DownGravity = 40f;

		[Header("Stamina")]
		public float MaxStamina = 100f;
		public float StaminaDrainPerSecond = 25f;
		public float StaminaRegenPerSecond = 35f;
		public float StaminaRegenDelay = 1f;
		public float JumpStaminaCost = 15f;
		[Tooltip("Stamina must reach this value before sprinting can start again after being fully depleted.")]
		public float MinStaminaToStartSprint = 5f;

		[Header("Movement Accelerations")]
		public float GroundAcceleration = 55f;
		public float GroundDeceleration = 25f;
		public float AirAcceleration = 25f;
		public float AirDeceleration = 1.3f;

		[Header("Fire Setup")]
		public LayerMask HitMask;
		public GameObject ImpactPrefab;
		[Tooltip("Effective range for the unarmed punch attack (meters).")]
		public float PunchRange = 1.5f;
		[Tooltip("Distance (meters) the target is pushed away from the attacker by an unarmed punch.")]
		public float PunchKnockbackDistance = 1.5f;
		[Tooltip("Minimum seconds between unarmed punches.")]
		public float PunchCooldown = 0.5f;

		[Header("Incoming Knockback")]
		[Tooltip("Constant deceleration (m/s²) applied to incoming knockback. Higher = snappier push. Peak push speed = sqrt(2 * deceleration * distance), duration = peakSpeed / deceleration.")]
		public float KnockbackDeceleration = 20f;

		[Header("Melee Charge")]
		[Tooltip("Seconds the attack button must be held before release applies the charged multipliers.")]
		public float ChargeThresholdSeconds = 2f;
		[Tooltip("Damage multiplier applied when a melee attack is released after holding for at least ChargeThresholdSeconds.")]
		public float ChargedDamageMultiplier = 2f;
		[Tooltip("Knockback distance multiplier applied to a fully-charged melee attack. Peak push speed and duration both scale with sqrt(distance).")]
		public float ChargedKnockbackMultiplier = 2f;

		[Header("Ragdoll")]
		[Tooltip("Knockback peak speed (m/s) at or above which the player goes ragdoll. Smaller hits just nudge.")]
		public float RagdollImpactThreshold = 5f;
		[Tooltip("Seconds added to the knocked-out duration per m/s of impact above the threshold.")]
		public float KnockoutSecondsPerUnit = 0.2f;
		[Tooltip("Minimum time spent knocked out once ragdoll is triggered.")]
		public float MinKnockoutDuration = 0.4f;
		[Tooltip("Maximum time spent knocked out, regardless of impact intensity.")]
		public float MaxKnockoutDuration = 3f;
		[Tooltip("Fixed time spent getting up after the knocked-out phase ends. Player input remains disabled during this phase.")]
		public float GetUpDuration = 1f;
		[Tooltip("Minimum |angular speed| (deg/s) of the random ragdoll tumble. Sign is chosen randomly so rolls go either direction.")]
		public float MinRagdollSpinSpeed = 270f;
		[Tooltip("Maximum |angular speed| (deg/s) of the random ragdoll tumble.")]
		public float MaxRagdollSpinSpeed = 720f;
		[Tooltip("How much vertical component the random tumble axis can have. 0 = pure horizontal roll, 1 = fully arbitrary. Small values feel ground-anchored.")]
		[Range(0f, 1f)]
		public float RagdollAxisVerticalJitter = 0.25f;
		[Tooltip("Per-second decay (deg/s²) of the tumble spin while in the Dead state, so the corpse eventually settles instead of rolling forever. Local-only.")]
		public float DeadRagdollSpinDecay = 360f;
		[Tooltip("Effective rolling radius (m) used to convert the ragdoll spin into horizontal travel while knocked out. Horizontal speed = |spinSpeed| * radius. 0 disables rolling translation.")]
		public float RagdollRollRadius = 0.5f;

		[Header("Animation Setup")]
		public Transform ChestTargetPosition;
		public Transform ChestBone;

		[Header("Sounds")]
		public AudioSource FireSound;
		public AudioSource FootstepSound;
		public AudioClip JumpAudioClip;
		public AudioClip LandAudioClip;

		[Header("VFX")]
		public ParticleSystem DustParticles;

		[Networked, HideInInspector, Capacity(24), OnChangedRender(nameof(OnNicknameChanged))]
		public string Nickname { get; set; }
		[Networked, HideInInspector]
		public int ChickenKills { get; set; }
		[Networked, HideInInspector]
		public float Stamina { get; private set; }

		[Networked]
		private Vector3 _moveVelocity { get; set; }
		[Networked, OnChangedRender(nameof(OnJumpingChanged))]
		private NetworkBool _isJumping { get; set; }
		[Networked]
		private NetworkBool _wasSprinting { get; set; }
		[Networked]
		private TickTimer _staminaRegenTimer { get; set; }
		[Networked]
		private Vector3 _hitPosition { get; set; }
		[Networked]
		private Vector3 _hitNormal { get; set; }
		[Networked]
		private int _fireCount { get; set; }
		[Networked]
		private Vector3 _knockbackDirection { get; set; }
		[Networked]
		private float _knockbackInitialSpeed { get; set; }
		[Networked]
		private float _knockbackDuration { get; set; }
		[Networked]
		private TickTimer _knockbackTimer { get; set; }
		[Networked]
		private TickTimer _fireCooldownTimer { get; set; }
		// 0 = not charging. Mutated only by state authority (and predicted on input authority via ProcessInput).
		[Networked]
		private int _meleeChargeStartTick { get; set; }
		[Networked]
		private NetworkBool _lastFireWasCharged { get; set; }

		[Networked, OnChangedRender(nameof(OnRagdollStateChanged))]
		public ERagdollState RagdollState { get; private set; }
		[Networked]
		private TickTimer _ragdollTimer { get; set; }
		[Networked]
		private Vector3 _ragdollTumbleAxis { get; set; }
		[Networked]
		private float _ragdollSpinSpeed { get; set; }
		// Get-up start/target angles are picked by the state authority at the KO → GettingUp
		// transition and replicated so every peer lerps from the same start to the same target.
		// Without this, per-peer frame-rate drift in _visualRollAngle could pick different
		// nearest-upright multiples and the body would visibly settle differently on each client.
		[Networked]
		private float _getUpStartAngle { get; set; }
		[Networked]
		private float _getUpTargetAngle { get; set; }

		/// <summary>True while the player is knocked out or getting up — input and active control are suppressed.</summary>
		public bool IsInputLocked => RagdollState != ERagdollState.Normal;

		private float _visualRollAngle;
		private float _deadSpinSpeedLocal;

		// Animation IDs
		private int _animIDSpeedX;
		private int _animIDSpeedZ;
		private int _animIDMoveSpeedZ;
		private int _animIDGrounded;
		private int _animIDPitch;
		private int _animIDShoot;

		private int _visibleFireCount;
		private Inventory _inventory;

		/// <summary>True while the player is mid-charge on a melee attack. Replicated.</summary>
		public bool IsMeleeCharging => _meleeChargeStartTick > 0;

		/// <summary>Seconds the current charge has been held. 0 when not charging.</summary>
		public float MeleeChargeSeconds
		{
			get
			{
				if (_meleeChargeStartTick <= 0 || Runner == null) return 0f;
				int elapsed = (int)Runner.Tick - _meleeChargeStartTick;
				return elapsed > 0 ? elapsed * Runner.DeltaTime : 0f;
			}
		}

		/// <summary>0..1 progress toward the charged threshold. Used to drive the pulled-back visual depth.</summary>
		public float MeleeChargeProgress => Mathf.Clamp01(MeleeChargeSeconds / Mathf.Max(0.01f, ChargeThresholdSeconds));

		public void Respawn(Vector3 position)
		{
			ChickenKills = 0;
			Health.Revive();

			KCC.SetActive(true);
			KCC.SetPosition(position);
			KCC.SetLookRotation(0f, 0f);

			_moveVelocity = Vector3.zero;
			Stamina = MaxStamina;
			_wasSprinting = false;
			_staminaRegenTimer = default;

			RagdollState = ERagdollState.Normal;
			_ragdollTimer = default;
			_ragdollTumbleAxis = Vector3.zero;
			_ragdollSpinSpeed = 0f;
			_knockbackDuration = 0f;
			_knockbackInitialSpeed = 0f;
			_knockbackTimer = default;

			_meleeChargeStartTick = 0;
			_lastFireWasCharged = false;
		}

		public override void Spawned()
		{
			// Player draws its own death visual via the ragdoll tilt — keep the body
			// visible past death instead of letting Health.Render hide VisualRoot.
			Health.SuppressDeathVisualSwap = true;

			if (HasStateAuthority)
			{
				Stamina = MaxStamina;
			}

			if (HasInputAuthority)
			{
				// Sending player nickname that is saved in UIGameMenu
				RPC_SetNickname(PlayerPrefs.GetString("PlayerName"));
			}

			// In case the nickname is already changed,
			// we need to trigger the change manually
			OnNicknameChanged();

			// Reset visible fire count
			_visibleFireCount = _fireCount;

			if (HasInputAuthority)
			{
				// For input authority deactivate head renderers so they are not obstructing the view
				for (int i = 0; i < HeadRenderers.Length; i++)
				{
					HeadRenderers[i].shadowCastingMode = ShadowCastingMode.ShadowsOnly;
				}

				// Held weapon is moved to FirstPersonOverlay layer by Inventory.RefreshHeldItem
				// to prevent clipping when close to a wall.

				// Look rotation interpolation is skipped for local player.
				// Look rotation is set manually in Render.
				KCC.Settings.ForcePredictedLookRotation = true;
			}
		}

		public override void FixedUpdateNetwork()
		{
			UpdateRagdollState();
			CheckDeathRagdoll();

			bool drainedThisTick = false;
			if (Health.IsAlive && RagdollState == ERagdollState.Normal && GetInput<GameplayInput>(out var input))
			{
				drainedThisTick = ProcessInput(input, Input.PreviousButtons);
			}
			else
			{
				// Continue with KCC movement (gravity + knockback decay) even
				// when player is dead, ragdolled, or input is missing.
				MovePlayer(Vector3.zero, 0f);
				_wasSprinting = false;
				// Cancel any in-progress charge — releasing while ragdolled/dead must not fire.
				if (HasStateAuthority && _meleeChargeStartTick != 0)
				{
					_meleeChargeStartTick = 0;
				}
			}

			if (drainedThisTick == false && _staminaRegenTimer.ExpiredOrNotRunning(Runner))
			{
				Stamina = Mathf.Min(MaxStamina, Stamina + StaminaRegenPerSecond * Runner.DeltaTime);
			}

			if (KCC.IsGrounded)
			{
				// Stop jumping
				_isJumping = false;
			}

			// Disable hits when dead so the corpse can't be hit again, but keep the KCC
			// active during the Dead ragdoll so gravity + collision keep the body falling
			// to the ground. Respawn() will reactivate the hitbox.
			HitboxRoot.HitboxRootActive = Health.IsAlive;
			KCC.SetActive(Health.IsAlive || RagdollState == ERagdollState.Dead);
		}

		public override void Render()
		{
			if (HasInputAuthority)
			{
				// Set look rotation for Render.
				KCC.SetLookRotation(Input.LookRotation, -90f, 90f);
			}

			// Transform velocity vector to local space.
			var moveSpeed = transform.InverseTransformVector(KCC.RealVelocity);

			Animator.SetFloat(_animIDSpeedX, moveSpeed.x, 0.1f, Time.deltaTime);
			Animator.SetFloat(_animIDSpeedZ, moveSpeed.z, 0.1f, Time.deltaTime);
			Animator.SetBool(_animIDGrounded, KCC.IsGrounded);
			Animator.SetFloat(_animIDPitch, KCC.GetLookRotation(true, false).x, 0.02f, Time.deltaTime);

			FootstepSound.enabled = KCC.IsGrounded && KCC.RealSpeed > 1f;
			ScalingRoot.localScale = Vector3.Lerp(ScalingRoot.localScale, Vector3.one, Time.deltaTime * 8f);

			var emission = DustParticles.emission;
			emission.enabled = KCC.IsGrounded && KCC.RealSpeed > 1f;

			ShowFireEffects();
			UpdateMeleeChargingVisual();
		}

		private void UpdateMeleeChargingVisual()
		{
			var held = GetHeldWeapon();
			var fist = held == null ? GetHeldFist() : null;
			bool charging = IsMeleeCharging;
			float progress = MeleeChargeProgress;

			if (held != null && held.IsMelee)
			{
				held.SetCharging(charging, progress);
			}
			else if (fist != null)
			{
				fist.SetCharging(charging, progress);
			}
		}

		private void Awake()
		{
			AssignAnimationIDs();
			_inventory = GetComponent<Inventory>();
		}

		private void LateUpdate()
		{
			bool isDeadRagdoll = RagdollState == ERagdollState.Dead;

			if (Health.IsAlive == false && isDeadRagdoll == false)
				return;

			// Update camera pivot (influences ChestIK)
			// (KCC look rotation is set earlier in Render)
			var pitchRotation = KCC.GetLookRotation(true, false);

			Quaternion ragdollTilt = ComputeRagdollTumbleRotation();
			CameraPivot.localRotation = ragdollTilt * Quaternion.Euler(pitchRotation);
			ScalingRoot.localRotation = ragdollTilt;

			if (isDeadRagdoll == false)
			{
				// Dummy IK solution, we are snapping chest bone to prepared ChestTargetPosition position
				// Lerping blends the fixed position with little bit of animation position.
				float blendAmount = HasInputAuthority ? 0.05f : 0.2f;
				ChestBone.position = Vector3.Lerp(ChestTargetPosition.position, ChestBone.position, blendAmount);
				ChestBone.rotation = Quaternion.Lerp(ChestTargetPosition.rotation, ChestBone.rotation, blendAmount);
			}

			// Only InputAuthority needs to update camera
			if (HasInputAuthority)
			{
				// Transfer properties from camera handle to Main Camera.
				Camera.main.transform.SetPositionAndRotation(CameraHandle.position, CameraHandle.rotation);
			}
		}

		private Quaternion ComputeRagdollTumbleRotation()
		{
			// Accumulate / unwind the visual roll angle from the networked tumble parameters.
			// Each peer drives its own _visualRollAngle from the same _ragdollSpinSpeed / Time.deltaTime,
			// so visuals stay in close lock-step without networking the running angle every tick.
			if (RagdollState == ERagdollState.KnockedOut)
			{
				_visualRollAngle += _ragdollSpinSpeed * Time.deltaTime;
				_deadSpinSpeedLocal = _ragdollSpinSpeed; // seed the dead-state decay if KO transitions to Dead
			}
			else if (RagdollState == ERagdollState.Dead)
			{
				// Decay the corpse's spin so it eventually settles instead of rolling forever.
				// Local-only — small per-peer variance on a corpse is fine.
				_deadSpinSpeedLocal = Mathf.MoveTowards(_deadSpinSpeedLocal, 0f, DeadRagdollSpinDecay * Time.deltaTime);
				_visualRollAngle += _deadSpinSpeedLocal * Time.deltaTime;
			}
			else if (RagdollState == ERagdollState.GettingUp)
			{
				// Smooth-step from the captured start angle to the nearest upright (computed in
				// OnRagdollStateChanged). SmoothStep eases at both ends so the body decelerates
				// into the final upright pose instead of arriving at constant angular velocity.
				float remaining = _ragdollTimer.RemainingTime(Runner) ?? 0f;
				float t = GetUpDuration > 0f ? 1f - Mathf.Clamp01(remaining / GetUpDuration) : 1f;
				float eased = Mathf.SmoothStep(0f, 1f, t);
				_visualRollAngle = Mathf.Lerp(_getUpStartAngle, _getUpTargetAngle, eased);
			}
			else // Normal
			{
				_visualRollAngle = 0f;
			}

			if (Mathf.Abs(_visualRollAngle) < 0.01f) return Quaternion.identity;
			if (_ragdollTumbleAxis.sqrMagnitude < 0.0001f) return Quaternion.identity;

			return Quaternion.AngleAxis(_visualRollAngle, _ragdollTumbleAxis);
		}

		private bool ProcessInput(GameplayInput input, NetworkButtons previousButtons)
		{
			KCC.SetLookRotation(input.LookRotation, -90f, 90f);

			// While a knockback impulse is in flight, suppress movement/sprint/jump input so the
			// player's self-driven velocity doesn't fight the knockback. Look rotation (above) and
			// Fire (below) stay active so the player can still aim and attack while sliding.
			bool knockbackActive = IsKnockbackActive();

			// Calculate correct move direction from input (rotated based on latest KCC rotation)
			var moveDirection = KCC.TransformRotation * new Vector3(input.MoveDirection.x, 0f, input.MoveDirection.y);

			bool wantsSprint = input.Buttons.IsSet(EInputButton.Sprint);
			bool isMoving = input.MoveDirection.sqrMagnitude > 0.01f;
			// Allow continuing a sprint as long as stamina > 0; require MinStaminaToStartSprint to (re)start.
			float startThreshold = _wasSprinting ? 0f : MinStaminaToStartSprint;
			bool isSprinting = !knockbackActive && wantsSprint && isMoving && Stamina > startThreshold;

			float speed = isSprinting ? SprintSpeed : WalkSpeed;
			var desiredMoveVelocity = knockbackActive ? Vector3.zero : moveDirection * speed;

			bool drained = false;
			if (isSprinting)
			{
				Stamina = Mathf.Max(0f, Stamina - StaminaDrainPerSecond * Runner.DeltaTime);
				_staminaRegenTimer = TickTimer.CreateFromSeconds(Runner, StaminaRegenDelay);
				drained = true;
			}
			_wasSprinting = isSprinting;

			float jumpImpulse = 0f;

			// Comparing current input buttons to previous input buttons - this prevents glitches when input is lost
			if (!knockbackActive && KCC.IsGrounded && input.Buttons.WasPressed(previousButtons, EInputButton.Jump))
			{
				// Set world space jump vector
				jumpImpulse = JumpImpulse;
				_isJumping = true;

				Stamina = Mathf.Max(0f, Stamina - JumpStaminaCost);
				_staminaRegenTimer = TickTimer.CreateFromSeconds(Runner, StaminaRegenDelay);
				drained = true;
			}

			if (knockbackActive)
			{
				// Hard-stop residual self-velocity so only the knockback impulse moves the player.
				// Without this, the lerp in MovePlayer would only decay _moveVelocity gradually.
				_moveVelocity = Vector3.zero;
			}

			MovePlayer(desiredMoveVelocity, jumpImpulse);

			// Update camera pivot so fire transform (CameraHandle) is correct
			var pitchRotation = KCC.GetLookRotation(true, false);
			CameraPivot.localRotation = Quaternion.Euler(pitchRotation);

			ProcessFireInput(input, previousButtons);

			return drained;
		}

		private void ProcessFireInput(GameplayInput input, NetworkButtons previousButtons)
		{
			// Resolve current melee context — held melee weapon, or bare fists (treated as melee).
			var heldNow = GetHeldWeapon();
			var fistNow = heldNow == null ? GetHeldFist() : null;
			bool isMeleeContext = (heldNow != null && heldNow.IsMelee) || fistNow != null;

			if (!isMeleeContext)
			{
				// Ranged (or empty hand with no fist): clear stale charge, fire on press.
				if (_meleeChargeStartTick != 0) _meleeChargeStartTick = 0;
				if (input.Buttons.WasPressed(previousButtons, EInputButton.Fire))
				{
					Fire(false);
				}
				return;
			}

			bool wasPressed = input.Buttons.WasPressed(previousButtons, EInputButton.Fire);
			bool wasReleased = input.Buttons.WasReleased(previousButtons, EInputButton.Fire);

			if (wasPressed && _meleeChargeStartTick == 0 && _fireCooldownTimer.ExpiredOrNotRunning(Runner))
			{
				_meleeChargeStartTick = Mathf.Max(1, (int)Runner.Tick);
			}
			else if (wasReleased && _meleeChargeStartTick != 0)
			{
				int elapsedTicks = (int)Runner.Tick - _meleeChargeStartTick;
				float chargeSeconds = Mathf.Max(0f, elapsedTicks * Runner.DeltaTime);
				bool charged = chargeSeconds >= ChargeThresholdSeconds;
				_meleeChargeStartTick = 0;
				Fire(charged);
			}
		}

		private void MovePlayer(Vector3 desiredMoveVelocity, float jumpImpulse)
		{
			// It feels better when the player falls quicker
			KCC.SetGravity(KCC.RealVelocity.y >= 0f ? UpGravity : DownGravity);

			float acceleration;
			if (desiredMoveVelocity == Vector3.zero)
			{
				// No desired move velocity - we are stopping
				acceleration = KCC.IsGrounded ? GroundDeceleration : AirDeceleration;
			}
			else
			{
				acceleration = KCC.IsGrounded ? GroundAcceleration : AirAcceleration;
			}

			_moveVelocity = Vector3.Lerp(_moveVelocity, desiredMoveVelocity, acceleration * Runner.DeltaTime);

			KCC.Move(_moveVelocity + ComputeKnockbackVelocity() + ComputeRagdollRollVelocity(), jumpImpulse);
		}

		// Rolling-body translation: while knocked out, derive a horizontal velocity from the
		// tumble spin so the body actually travels across the ground instead of spinning in
		// place. Deterministic — all inputs are [Networked], so each peer's KCC moves identically.
		private Vector3 ComputeRagdollRollVelocity()
		{
			if (RagdollState != ERagdollState.KnockedOut) return Vector3.zero;
			if (RagdollRollRadius <= 0f) return Vector3.zero;

			Vector3 axisHoriz = _ragdollTumbleAxis;
			axisHoriz.y = 0f;
			if (axisHoriz.sqrMagnitude < 0.0001f) return Vector3.zero;
			axisHoriz.Normalize();

			Vector3 perp = Vector3.Cross(Vector3.up, axisHoriz);
			float radSpeed = _ragdollSpinSpeed * Mathf.Deg2Rad;
			return perp * (radSpeed * RagdollRollRadius);
		}

		public void ApplyKnockback(Vector3 fromPosition, float distance)
		{
			// Same authority model as Health.TakeHit: callers (e.g. Fire) run on every
			// predicting peer; only state authority actually mutates [Networked] state.
			if (HasStateAuthority == false) return;
			if (distance <= 0f || KnockbackDeceleration <= 0f) return;

			// Constant-deceleration model with linear velocity decay:
			//   covered distance = peakSpeed * duration / 2, and peakSpeed = deceleration * duration
			//   → peakSpeed = sqrt(2 * deceleration * distance), duration = peakSpeed / deceleration.
			float peakSpeed = Mathf.Sqrt(2f * KnockbackDeceleration * distance);
			float duration = peakSpeed / KnockbackDeceleration;

			Vector3 dir = transform.position - fromPosition;
			dir.y = 0f;
			if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
			dir.Normalize();

			_knockbackDirection = dir;
			_knockbackDuration = duration;
			_knockbackInitialSpeed = peakSpeed;
			_knockbackTimer = TickTimer.CreateFromSeconds(Runner, duration);

			ApplyImpact(dir * peakSpeed);
		}

		/// <summary>
		/// Apply an instantaneous impact velocity (m/s, world space). If the peak horizontal speed
		/// reaches <see cref="RagdollImpactThreshold"/> the player goes ragdoll for a duration scaled
		/// by intensity, followed by a fixed <see cref="GetUpDuration"/> getting-up phase during which
		/// input remains disabled. State-authority-only — clients calling this will silently no-op.
		/// </summary>
		public void ApplyImpact(Vector3 worldVelocity)
		{
			if (HasStateAuthority == false) return;

			Vector3 horizontal = worldVelocity;
			horizontal.y = 0f;
			float speed = horizontal.magnitude;
			if (speed < RagdollImpactThreshold) return;
			if (RagdollState == ERagdollState.GettingUp) return; // already recovering

			RandomizeRagdollTumble();

			float koDuration = Mathf.Clamp(
				(speed - RagdollImpactThreshold) * KnockoutSecondsPerUnit,
				MinKnockoutDuration,
				MaxKnockoutDuration);

			RagdollState = ERagdollState.KnockedOut;
			_ragdollTimer = TickTimer.CreateFromSeconds(Runner, koDuration);
		}

		/// <summary>
		/// Picks a random tumble axis (mostly horizontal) and signed angular speed used to
		/// drive the visual ragdoll roll on every peer. State-authority-only — the chosen
		/// values flow to clients via the [Networked] fields so all peers see the same tumble.
		/// </summary>
		private void RandomizeRagdollTumble()
		{
			Vector3 axis = new Vector3(
				Random.Range(-1f, 1f),
				Random.Range(-RagdollAxisVerticalJitter, RagdollAxisVerticalJitter),
				Random.Range(-1f, 1f));
			if (axis.sqrMagnitude < 0.0001f) axis = Vector3.right;
			_ragdollTumbleAxis = axis.normalized;

			float magnitude = Random.Range(MinRagdollSpinSpeed, MaxRagdollSpinSpeed);
			_ragdollSpinSpeed = Random.value < 0.5f ? -magnitude : magnitude;
		}

		private void UpdateRagdollState()
		{
			if (HasStateAuthority == false) return;

			switch (RagdollState)
			{
				case ERagdollState.KnockedOut:
					// Wait for the knockout timer AND for the player to land before standing back up.
					if (_ragdollTimer.ExpiredOrNotRunning(Runner) && KCC.IsGrounded)
					{
						// Snapshot state-authority's local roll into the networked fields BEFORE
						// flipping the state, so OnRagdollStateChanged on every peer reads
						// matching values and lerps from the same start to the same target.
						_getUpStartAngle = _visualRollAngle;
						_getUpTargetAngle = Mathf.Round(_visualRollAngle / 360f) * 360f;

						RagdollState = ERagdollState.GettingUp;
						_ragdollTimer = TickTimer.CreateFromSeconds(Runner, GetUpDuration);
					}
					break;
				case ERagdollState.GettingUp:
					if (_ragdollTimer.ExpiredOrNotRunning(Runner))
					{
						RagdollState = ERagdollState.Normal;
						_ragdollTimer = default;
					}
					break;
				// Dead is sticky — Respawn() is what clears it.
			}
		}

		private void CheckDeathRagdoll()
		{
			if (HasStateAuthority == false) return;
			if (Health.IsAlive) return;
			if (RagdollState == ERagdollState.Dead) return;

			// Preserve the existing tumble if a fatal hit just knocked us out; otherwise pick a fresh
			// random tumble so even stationary deaths fall in a varied direction.
			if (_ragdollTumbleAxis.sqrMagnitude < 0.0001f)
			{
				RandomizeRagdollTumble();
			}

			RagdollState = ERagdollState.Dead;
			_ragdollTimer = default;
		}

		private Vector3 ComputeKnockbackVelocity()
		{
			if (_knockbackDuration <= 0f) return Vector3.zero;
			float? remaining = _knockbackTimer.RemainingTime(Runner);
			if (remaining == null || remaining.Value <= 0f) return Vector3.zero;
			float t = Mathf.Clamp01(remaining.Value / _knockbackDuration);
			return _knockbackDirection * (_knockbackInitialSpeed * t);
		}

		private bool IsKnockbackActive()
		{
			if (_knockbackDuration <= 0f) return false;
			float? remaining = _knockbackTimer.RemainingTime(Runner);
			return remaining.HasValue && remaining.Value > 0f;
		}

		private void Fire(bool charged)
		{
			var held = GetHeldWeapon();
			var fist = held == null ? GetHeldFist() : null;

			// Nothing to fire/punch with.
			if (held == null && fist == null) return;

			// Enforce per-weapon (or punch) cooldown. Runs on both state and input authority
			// during FUN, so prediction and authoritative state agree on rate-limiting.
			if (_fireCooldownTimer.ExpiredOrNotRunning(Runner) == false) return;

			float range = held != null ? held.Range : PunchRange;
			int damage = held != null ? held.Damage : 1;
			float cooldown = held != null ? held.Cooldown : PunchCooldown;

			float kbDistance = 0f;
			if (held != null && held.IsMelee)
			{
				kbDistance = held.KnockbackDistance;
			}
			else if (held == null && fist != null)
			{
				kbDistance = PunchKnockbackDistance;
			}

			// Charged release: melee-only boost (ranged callers always pass charged=false).
			bool isMelee = (held != null && held.IsMelee) || fist != null;
			if (charged && isMelee)
			{
				damage = Mathf.Max(1, Mathf.RoundToInt(damage * ChargedDamageMultiplier));
				kbDistance *= ChargedKnockbackMultiplier;
			}

			_lastFireWasCharged = charged && isMelee;

			if (cooldown > 0f)
			{
				_fireCooldownTimer = TickTimer.CreateFromSeconds(Runner, cooldown);
			}

			// Clear hit position in case nothing will be hit
			_hitPosition = Vector3.zero;

			var hitOptions = HitOptions.IncludePhysX | HitOptions.IgnoreInputAuthority;

			// Whole projectile path and effects are immediately processed (= hitscan projectile)
			if (Runner.LagCompensation.Raycast(CameraHandle.position, CameraHandle.forward, range,
				    Object.InputAuthority, out var hit, HitMask, hitOptions, QueryTriggerInteraction.Ignore) == true)
			{
				// Deal damage
				var health = hit.Hitbox != null ? hit.Hitbox.Root.GetComponent<Health>() : null;
				if (health != null && health.TakeHit(damage))
				{
					if (kbDistance > 0f)
					{
						var knockable = hit.Hitbox.Root.GetComponent<IKnockbackable>();
						knockable?.ApplyKnockback(transform.position, kbDistance);
					}

					if (health.IsAlive == false)
					{
						// Killing chicken grants 1 point, killing other player has -10 points penalty.
						ChickenKills += health.GetComponent<Chicken>() != null ? 1 : -10;
					}
				}

				// Save hit point to correctly show bullet path on all clients.
				// This however works only for single projectile per FUN and with higher fire cadence
				// some projectiles might not be fired on proxies because we save only the position
				// of the LAST hit.
				_hitPosition = hit.Point;
				_hitNormal = hit.Normal;
			}

			// In this example projectile count property (fire count) is used not only for weapon fire effects
			// but to spawn the projectile visuals themselves.
			_fireCount++;
		}

		private void ShowFireEffects()
		{
			// Notice we are not using OnChangedRender for fireCount property but instead
			// we are checking against a local variable and show fire effects only when visible
			// fire count is SMALLER. This prevents triggering false fire effects when
			// local player mispredicted fire (e.g. input got lost) and fireCount property got decreased.
			if (_visibleFireCount < _fireCount)
			{
				var held = GetHeldWeapon();
				var fist = held == null ? GetHeldFist() : null;

				if (held != null)
				{
					held.PlayAttackSound();
					if (held.IsMelee)
					{
						held.Swing(_lastFireWasCharged);
					}
					else
					{
						if (held.MuzzleParticle != null) held.MuzzleParticle.Play();
						held.Recoil();
					}
				}
				else if (fist != null)
				{
					fist.Punch(_lastFireWasCharged);
				}

				Animator.SetTrigger(_animIDShoot);

				if (_hitPosition != Vector3.zero)
				{
					// Impact gets destroyed automatically with DestroyAfter script
					Instantiate(ImpactPrefab, _hitPosition, Quaternion.LookRotation(_hitNormal));
				}
			}

			_visibleFireCount = _fireCount;
		}

		private HeldWeapon GetHeldWeapon()
		{
			if (_inventory == null) return null;
			var instance = _inventory.HeldInstance;
			return instance != null ? instance.GetComponent<HeldWeapon>() : null;
		}

		private FistPunchAnimator GetHeldFist()
		{
			if (_inventory == null) return null;
			var instance = _inventory.HeldInstance;
			return instance != null ? instance.GetComponent<FistPunchAnimator>() : null;
		}

		private void AssignAnimationIDs()
		{
			_animIDSpeedX = Animator.StringToHash("SpeedX");
			_animIDSpeedZ = Animator.StringToHash("SpeedZ");
			_animIDGrounded = Animator.StringToHash("Grounded");
			_animIDPitch = Animator.StringToHash("Pitch");
			_animIDShoot = Animator.StringToHash("Shoot");
		}

		private void OnJumpingChanged()
		{
			if (_isJumping)
			{
				AudioSource.PlayClipAtPoint(JumpAudioClip, KCC.Position, 0.5f);
			}
			else
			{
				AudioSource.PlayClipAtPoint(LandAudioClip, KCC.Position, 1f);
			}

			if (HasInputAuthority == false)
			{
				ScalingRoot.localScale = _isJumping ? new Vector3(0.5f, 1.5f, 0.5f) : new Vector3(1.25f, 0.75f, 1.25f);
			}
		}

		private void OnNicknameChanged()
		{
			if (HasInputAuthority)
				return; // Do not show nickname for local player

			Nameplate.SetNickname(Nickname);
		}

		private void OnRagdollStateChanged()
		{
			if (RagdollState == ERagdollState.GettingUp)
			{
				// _getUpStartAngle / _getUpTargetAngle are set by the state authority in
				// UpdateRagdollState and replicated, so every peer lerps from the same start to
				// the same target. Snap the local roll to the networked start to absorb any
				// per-peer frame-rate drift on this single transition frame.
				_visualRollAngle = _getUpStartAngle;
			}
			else if (RagdollState == ERagdollState.Dead)
			{
				// Seed the dead-spin decay from the networked spin speed. The KO branch in
				// ComputeRagdollTumbleRotation seeds this each frame for KO → Dead transitions,
				// but a direct Normal → Dead transition (instant kill, no prior ragdoll) would
				// leave it at 0 and the corpse would freeze upright instead of tumbling.
				_deadSpinSpeedLocal = _ragdollSpinSpeed;
			}
			else if (RagdollState == ERagdollState.Normal)
			{
				_visualRollAngle = 0f;
				_deadSpinSpeedLocal = 0f;
			}
		}

		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
		private void RPC_SetNickname(string nickname)
		{
			Nickname = nickname;
		}
	}
}
