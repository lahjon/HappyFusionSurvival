using UnityEngine;
using Fusion;
using Fusion.Addons.SimpleKCC;
using Starter.Common.Inventory;
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
		public ActionInvoker ActionInvoker;

		[Header("Movement Setup")]
		public float WalkSpeed = 2f;
		public float SprintSpeed = 8f;
		public float JumpImpulse = 10f;
		public float UpGravity = 25f;
		public float DownGravity = 40f;

		[Header("Crouch")]
		[Tooltip("Horizontal speed while crouched and grounded (m/s). Airborne movement keeps the normal walk/sprint speed.")]
		public float CrouchSpeed = 1.2f;
		[Tooltip("KCC capsule height while crouched (meters). The standing height is read from the KCC on Spawned and restored when standing.")]
		public float CrouchHeight = 1.0f;
		[Tooltip("Local-space vertical offset applied to CameraPivot while crouched (negative = lower). Local-only visual.")]
		public float CrouchCameraDrop = -0.5f;
		[Tooltip("How quickly the local camera eases between standing and crouched height.")]
		public float CrouchCameraLerpSpeed = 12f;
		[Tooltip("Upward clearance (meters above the KCC root) required to stand back up. Releasing Crouch while a ceiling is overhead keeps the player crouched.")]
		public float CrouchStandClearance = 1.8f;
		[Tooltip("Radius of the ceiling clearance sphere check. Should be ~KCC radius so a narrow gap above the head still blocks stand-up.")]
		public float CrouchStandCheckRadius = 0.3f;

		[Header("Stamina")]
		public float MaxStamina = 100f;
		public float StaminaDrainPerSecond = 25f;
		public float StaminaRegenPerSecond = 35f;
		public float StaminaRegenDelay = 1f;
		public float JumpStaminaCost = 15f;
		[Tooltip("Stamina must reach this value before sprinting can start again after being fully depleted.")]
		public float MinStaminaToStartSprint = 5f;

		[Header("Hunger")]
		[Tooltip("Maximum fullness value. Hunger starts here on spawn/respawn and is the cap restored by food.")]
		public float MaxHunger = 100f;
		[Tooltip("Hunger drained per 5 seconds passively (constant background tick). 1 means 1 hunger every 5s; at MaxHunger=100 that's ~8.3 minutes to starve at rest.")]
		public float HungerDrainPer5Seconds = 1f;
		[Tooltip("Extra hunger drained per point of stamina spent this tick (sprint/jump/climb). Small by design — the 'work makes you hungrier' nudge.")]
		public float HungerPerStaminaPoint = 0.005f;

		[Header("Climbing")]
		[Tooltip("Surfaces on these layers can be climbed.")]
		public LayerMask ClimbableMask;
		[Tooltip("Movement speed along the wall plane (m/s).")]
		public float ClimbSpeed = 2.5f;
		[Tooltip("Speed of the climb-hop burst. Direction is set by WASD (hop along the wall) or by an empty input (back-flip off the wall).")]
		public float ClimbJumpImpulse = 4f;
		[Tooltip("Seconds between climb-hops. Must be greater than ClimbHopBurstDuration.")]
		public float ClimbJumpCooldown = 0.35f;
		[Tooltip("How long the upward burst velocity is applied during a climb-hop. The cooldown timer is shared, so the first ClimbHopBurstDuration seconds of the timer are the motion phase, the rest is recovery.")]
		public float ClimbHopBurstDuration = 0.15f;
		[Tooltip("Stamina drained per second while holding still on the wall (BOTW does drain when idle).")]
		public float ClimbStaminaIdleDrain = 4f;
		[Tooltip("Stamina drained per second while actively moving on the wall.")]
		public float ClimbStaminaMoveDrain = 12f;
		[Tooltip("Downward slide speed (m/s) along the wall while stamina is depleted. The player stays attached but cannot climb up, hop, or leap until they ClimbDrop or slide off the bottom.")]
		public float ClimbDepletedSlideSpeed = 1.5f;
		[Tooltip("Seconds the player remains attached after stamina depletes. Once this expires they let go and fall normally.")]
		public float ClimbDepletedHoldDuration = 1f;
		[Tooltip("Flat stamina cost of a climb-hop.")]
		public float ClimbStaminaJumpCost = 20f;
		[Tooltip("Horizontal launch speed (m/s) of a wall-leap. A wall-leap fires when Jump is pressed while looking away from the wall — meant for jumping toward a neighboring wall.")]
		public float WallLeapHorizontalSpeed = 7f;
		[Tooltip("Vertical impulse applied to a wall-leap. Higher = more arc, easier to reach walls slightly above the launch point.")]
		public float WallLeapJumpImpulse = 9f;
		[Tooltip("Forward raycast length used to detect/probe the climbable wall (meters, from chest).")]
		public float ClimbWallProbeDistance = 0.55f;
		[Tooltip("Distance to keep the player away from the wall surface while anchored.")]
		public float ClimbStickRange = 0.4f;
		[Tooltip("Minimum surface angle (degrees from horizontal) to count as a wall. Below this it's treated as a floor/ramp and rejected.")]
		public float MaxClimbSurfaceAngle = 80f;
		[Tooltip("Duration of the mantle animation onto a ledge.")]
		public float MantleDuration = 0.6f;
		[Tooltip("How far forward (beyond the wall normal) the mantle ends.")]
		public float MantleForwardDistance = 0.7f;
		[Tooltip("How far up the mantle lifts the player.")]
		public float MantleUpDistance = 1.1f;
		[Tooltip("While climbing, if a flat ledge top is within this distance above the chest, the mantle triggers early instead of waiting for the chest to clear the wall. Higher = grabs onto ledges from further below.")]
		public float LedgeReachDistance = 0.8f;

		[Header("Movement Accelerations")]
		public float GroundAcceleration = 55f;
		public float GroundDeceleration = 25f;
		public float AirAcceleration = 25f;
		public float AirDeceleration = 1.3f;

		[Header("Fire Setup")]
		[Tooltip("Visual prefab spawned at the hit point on a successful attack.")]
		public GameObject ImpactPrefab;

		[Header("Incoming Knockback")]
		[Tooltip("Constant deceleration (m/s²) applied to incoming knockback. Higher = snappier push. Peak push speed = sqrt(2 * deceleration * distance), duration = peakSpeed / deceleration.")]
		public float KnockbackDeceleration = 20f;

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

		[Header("Head Bob (local-only)")]
		[Tooltip("Vertical bob amplitude (meters) while walking.")]
		public float WalkBobAmplitude = 0.035f;
		[Tooltip("Vertical bob amplitude (meters) while sprinting.")]
		public float SprintBobAmplitude = 0.075f;
		[Tooltip("Bob frequency (Hz) while walking.")]
		public float WalkBobFrequency = 1.8f;
		[Tooltip("Bob frequency (Hz) while sprinting.")]
		public float SprintBobFrequency = 2.6f;
		[Tooltip("Horizontal sway as a fraction of the vertical amplitude.")]
		public float BobHorizontalRatio = 0.5f;
		[Tooltip("How quickly the bob fades in/out when starting or stopping movement.")]
		public float BobBlendSpeed = 8f;

		[Header("Camera FOV (local-only)")]
		[Tooltip("Degrees added on top of the camera's base FOV at full sprint speed, to sell the sensation of speed.")]
		public float SprintFOVBoost = 8f;
		[Tooltip("How quickly the FOV eases toward its target when sprint starts or stops.")]
		public float FOVLerpSpeed = 6f;

		[Header("Camera Collision (local-only)")]
		[Tooltip("Layers that should block the camera. Set this to the environment layers (walls, terrain) and EXCLUDE the player's own layer so the cast doesn't self-hit.")]
		public LayerMask CameraCollisionMask = ~0;
		[Tooltip("Sphere radius used for the pivot→camera sweep. Should be ≥ the camera's near-plane corner radius so the near plane never pokes through a surface.")]
		public float CameraCollisionRadius = 0.15f;
		[Tooltip("Extra clearance kept between the camera and any blocking surface.")]
		public float CameraCollisionPadding = 0.05f;

		[Networked, HideInInspector, Capacity(24), OnChangedRender(nameof(OnNicknameChanged))]
		public string Nickname { get; set; }
		[Networked, HideInInspector]
		public int ChickenKills { get; set; }
		[Networked, HideInInspector]
		public float Stamina { get; private set; }
		[Networked, HideInInspector]
		public float Hunger { get; private set; }
		/// <summary>True while the player is crouched. Replicated so proxies shrink the capsule and mirror posture.</summary>
		[Networked, HideInInspector]
		public NetworkBool IsCrouching { get; private set; }

		/// <summary>Effective stamina ceiling — naturally MaxStamina, but capped at the current Hunger so a starving player has a shrunken stamina bar.</summary>
		public float EffectiveMaxStamina => Mathf.Min(MaxStamina, Hunger);

		[Networked]
		private Vector3 _moveVelocity { get; set; }
		[Networked, OnChangedRender(nameof(OnJumpingChanged))]
		private NetworkBool _isJumping { get; set; }
		[Networked]
		private NetworkBool _wasSprinting { get; set; }
		[Networked]
		private TickTimer _staminaRegenTimer { get; set; }

		/// <summary>The seat the player is currently in (driver or passenger), or null if on foot. Set by <see cref="Seat.RPC_RequestEnter"/> on state authority.</summary>
		[Networked, OnChangedRender(nameof(OnInCurrentSeatChanged))]
		public Seat InCurrentSeat { get; set; }

		/// <summary>True while occupying any seat. Drives input gating, capsule deactivation, and the seated render/camera path.</summary>
		public bool IsSeated => InCurrentSeat != null;

		/// <summary>True while the player is sleeping in a Bed. Sleep is local-camera-only — the body stays in place but movement, input, and damage are suppressed; the bed's <c>SleepSession</c> owns the camera.</summary>
		[Networked, OnChangedRender(nameof(OnSleepingChanged))]
		public NetworkBool IsSleeping { get; private set; }

		/// <summary>Per-player ability gate. When false the player cannot enter climb. Defaults true; gameplay (item/perk) may toggle.</summary>
		[Networked, OnChangedRender(nameof(OnCanClimbChanged))]
		public NetworkBool CanClimb { get; set; } = true;
		/// <summary>True while the player is anchored to a wall.</summary>
		[Networked, OnChangedRender(nameof(OnIsClimbingChanged))]
		public NetworkBool IsClimbing { get; private set; }
		[Networked]
		private Vector3 _climbWallNormal { get; set; }
		[Networked]
		private TickTimer _climbJumpTimer { get; set; }
		// Starts when stamina hits zero while climbing. While it ticks, the player slides slowly
		// down; when it expires they let go and fall.
		[Networked]
		private TickTimer _climbSlideTimer { get; set; }
		// Non-default _mantleTimer means a mantle is in progress.
		[Networked]
		private TickTimer _mantleTimer { get; set; }
		[Networked]
		private Vector3 _mantleStart { get; set; }
		[Networked]
		private Vector3 _mantleEnd { get; set; }

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

		/// <summary>True while a mantle animation is in progress (input is suppressed during mantle).</summary>
		public bool IsMantling
		{
			get
			{
				if (Runner == null) return false;
				float? remaining = _mantleTimer.RemainingTime(Runner);
				return remaining.HasValue && remaining.Value > 0f;
			}
		}

		/// <summary>Surface normal of the wall the player is currently anchored to. Zero when not climbing.</summary>
		public Vector3 ClimbWallNormal => _climbWallNormal;
		/// <summary>World-space start position of the current mantle (set when the timer starts).</summary>
		public Vector3 MantleStart => _mantleStart;
		/// <summary>World-space end position of the current mantle (the ledge target).</summary>
		public Vector3 MantleEnd => _mantleEnd;
		/// <summary>0..1 progress through the current mantle. 0 when not mantling.</summary>
		public float MantleProgress
		{
			get
			{
				if (Runner == null || MantleDuration <= 0f) return 0f;
				float? remaining = _mantleTimer.RemainingTime(Runner);
				if (!remaining.HasValue) return 0f;
				return 1f - Mathf.Clamp01(remaining.Value / MantleDuration);
			}
		}

		/// <summary>True while the player is knocked out, getting up, mantling, seated in a vehicle, or sleeping — input and active control are suppressed.</summary>
		public bool IsInputLocked => RagdollState != ERagdollState.Normal || IsMantling || IsSeated || IsSleeping;

		private float _visualRollAngle;
		private float _deadSpinSpeedLocal;
		private float _bobPhase;
		private float _bobWeight;
		private float _baseFOV = -1f;
		// Standing capsule height and camera-pivot offset, captured on Spawned so SetHeight / camera
		// drop can be cleanly unwound when standing back up.
		private float _standHeight = -1f;
		private Vector3 _standCameraPivotLocalPos;

		// Animation IDs
		private int _animIDSpeedX;
		private int _animIDSpeedZ;
		private int _animIDMoveSpeedZ;
		private int _animIDGrounded;
		private int _animIDPitch;
		private int _animIDShoot;

		private int _visibleFireCount;
		private Inventory _inventory;

		public void Respawn(Vector3 position)
		{
			ChickenKills = 0;
			Health.Revive();

			KCC.SetActive(true);
			KCC.SetPosition(position);
			KCC.SetLookRotation(0f, 0f);

			_moveVelocity = Vector3.zero;
			Stamina = MaxStamina;
			Hunger = MaxHunger;
			_wasSprinting = false;
			_staminaRegenTimer = default;

			RagdollState = ERagdollState.Normal;
			_ragdollTimer = default;
			_ragdollTumbleAxis = Vector3.zero;
			_ragdollSpinSpeed = 0f;
			_knockbackDuration = 0f;
			_knockbackInitialSpeed = 0f;
			_knockbackTimer = default;

			IsClimbing = false;
			_climbWallNormal = Vector3.zero;
			_climbJumpTimer = default;
			_climbSlideTimer = default;
			_mantleTimer = default;
			_mantleStart = Vector3.zero;
			_mantleEnd = Vector3.zero;

			IsCrouching = false;
			if (_standHeight > 0f) KCC.SetHeight(_standHeight);

			if (ActionInvoker != null)
			{
				ActionInvoker.CancelCharge();
			}
		}

		/// <summary>Restore hunger (fullness). Called by FoodConsumable.Apply on every predicting peer — state-authority-only mutation per Fusion conventions.</summary>
		public void AddHunger(float amount)
		{
			if (HasStateAuthority == false) return;
			if (amount <= 0f) return;
			Hunger = Mathf.Min(MaxHunger, Hunger + amount);
		}

		public override void Spawned()
		{
			// Player draws its own death visual via the ragdoll tilt — keep the body
			// visible past death instead of letting Health.Render hide VisualRoot.
			Health.SuppressDeathVisualSwap = true;

			// Re-seed SimpleKCC's internal pose from the spawn position. Without this, the KCC's
			// first-tick depenetration can run from the prefab's serialized transform (often 0,0,0)
			// instead of the spawn point and tunnel the player through the floor.
			KCC.SetActive(true);
			KCC.SetPosition(transform.position);
			KCC.SetLookRotation(0f, 0f);

			_standHeight = KCC.Settings.Height;
			if (CameraPivot != null) _standCameraPivotLocalPos = CameraPivot.localPosition;

			if (HasStateAuthority)
			{
				Stamina = MaxStamina;
				Hunger = MaxHunger;
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
			// Mirror IsSleeping onto Health so TakeHit short-circuits while a player is in a bed.
			// Set every tick on every peer (cheap, idempotent) so authority always sees the right value
			// regardless of when OnSleepingChanged fired relative to the damage call.
			if (Health != null) Health.IsInvulnerable = IsSleeping;

			bool seatedNow = IsSeated;
			// Dying while seated: pop the player out of the vehicle so the normal death/respawn flow
			// can run. The seated branch returns early and skips CheckDeathRagdoll/UpdateRagdollState,
			// so without this an HP=0 seated player would never enter the Dead ragdoll state and
			// Respawn() would put them back into a still-occupied seat.
			if (seatedNow && HasStateAuthority && Health.IsAlive == false)
			{
				InCurrentSeat.HostForceRelease();
				seatedNow = false;
			}

			if (seatedNow)
			{
				UpdateSeated();
				HitboxRoot.HitboxRootActive = false;
				return;
			}

			UpdateRagdollState();
			CheckDeathRagdoll();

			float staminaAtTickStart = Stamina;
			bool drainedThisTick = false;
			if (IsMantling)
			{
				// Mantle takes priority over normal input handling — the player is in a forced
				// animation finishing the climb-up onto the ledge. Input is locked via IsInputLocked.
				ProcessMantle();
			}
			else if (Health.IsAlive && RagdollState == ERagdollState.Normal && IsSleeping == false && GetInput<GameplayInput>(out var input))
			{
				drainedThisTick = IsClimbing
					? ProcessClimbInput(input, Input.PreviousButtons)
					: ProcessInput(input, Input.PreviousButtons);
			}
			else
			{
				// Continue with KCC movement (gravity + knockback decay) even
				// when player is dead, ragdolled, or input is missing.
				MovePlayer(Vector3.zero, 0f);
				_wasSprinting = false;
				// Drop out of climb if it was active — knockback / ragdoll must not strand the player on a wall.
				if (HasStateAuthority && IsClimbing)
				{
					ExitClimb();
				}
				// Cancel any in-progress charge — releasing while ragdolled/dead must not fire.
				if (HasStateAuthority && ActionInvoker != null && ActionInvoker.IsCharging)
				{
					ActionInvoker.CancelCharge();
				}
				// Ragdolled / dead / dropped-input — force uncrouch so the body doesn't stay shrunken
				// during get-up or after respawn. Capsule height is restored by ApplyCrouchHeight.
				if (IsCrouching) IsCrouching = false;
				ApplyCrouchHeight();
			}

			if (drainedThisTick == false && _staminaRegenTimer.ExpiredOrNotRunning(Runner))
			{
				Stamina = Mathf.Min(EffectiveMaxStamina, Stamina + StaminaRegenPerSecond * Runner.DeltaTime);
			}

			// Hunger drain: passive tick (rate is "per 5 seconds" → divide by 5 for per-second) + a small per-stamina-point cost so sprinting/climbing makes you hungrier faster.
			float staminaBurnedThisTick = Mathf.Max(0f, staminaAtTickStart - Stamina);
			float hungerDelta = HungerDrainPer5Seconds * (Runner.DeltaTime / 5f) + staminaBurnedThisTick * HungerPerStaminaPoint;
			Hunger = Mathf.Max(0f, Hunger - hungerDelta);

			// Max stamina is capped at current hunger — clamp the live value down so the bar shrinks as you starve.
			if (Stamina > Hunger) Stamina = Hunger;

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
			if (IsSeated)
			{
				SeatedRender();
				return;
			}

			if (HasInputAuthority)
			{
				// Set look rotation for Render.
				KCC.SetLookRotation(Input.LookRotation, -90f, 90f);
			}

			// Hide the held weapon visual whenever the player is on a wall or mantling. Driven every
			// render frame rather than only on the OnChangedRender hook, so a missed change event
			// (race against Awake / first-frame ordering) can't strand the weapon visible.
			if (_inventory != null)
			{
				bool shouldSuppressHeld = IsClimbing || IsMantling;
				if (_inventory.SuppressHeldVisual != shouldSuppressHeld)
				{
					_inventory.SuppressHeldVisual = shouldSuppressHeld;
				}
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
			var provider = GetActionProvider();
			if (provider == null) return;

			var action = GetActiveAction(provider);
			if (action == null || action.Charge.Enabled == false)
			{
				provider.SetCharging(false, 0f);
				return;
			}

			provider.SetCharging(ActionInvoker.IsCharging, ActionInvoker.ChargeProgress(action));
		}

		private void Awake()
		{
			AssignAnimationIDs();
			_inventory = GetComponent<Inventory>();
			if (ActionInvoker == null) ActionInvoker = GetComponent<ActionInvoker>();
			// Auto-attach the procedural climbing-hand visual so the Player prefab doesn't need editing.
			if (GetComponent<ClimbingHands>() == null) gameObject.AddComponent<ClimbingHands>();
		}

		private void LateUpdate()
		{
			if (IsSeated)
			{
				SeatedLateUpdate();
				return;
			}

			bool isDeadRagdoll = RagdollState == ERagdollState.Dead;

			if (Health.IsAlive == false && isDeadRagdoll == false)
				return;

			// Update camera pivot (influences ChestIK)
			// (KCC look rotation is set earlier in Render)
			var pitchRotation = KCC.GetLookRotation(true, false);

			Quaternion ragdollTilt = ComputeRagdollTumbleRotation();
			CameraPivot.localRotation = ragdollTilt * Quaternion.Euler(pitchRotation);
			ScalingRoot.localRotation = ragdollTilt;

			// Local-only: ease the camera pivot toward its crouched / standing local position.
			// Driven from the networked IsCrouching flag so the lerp starts the moment the
			// state replicates. Standing pose is captured on Spawned.
			if (HasInputAuthority && _standHeight > 0f)
			{
				Vector3 target = _standCameraPivotLocalPos + (IsCrouching ? new Vector3(0f, CrouchCameraDrop, 0f) : Vector3.zero);
				CameraPivot.localPosition = Vector3.Lerp(CameraPivot.localPosition, target, CrouchCameraLerpSpeed * Time.deltaTime);
			}

			if (isDeadRagdoll == false)
			{
				// Dummy IK solution, we are snapping chest bone to prepared ChestTargetPosition position
				// Lerping blends the fixed position with little bit of animation position.
				float blendAmount = HasInputAuthority ? 0.05f : 0.2f;
				ChestBone.position = Vector3.Lerp(ChestTargetPosition.position, ChestBone.position, blendAmount);
				ChestBone.rotation = Quaternion.Lerp(ChestTargetPosition.rotation, ChestBone.rotation, blendAmount);
			}

			// Only InputAuthority needs to update camera. ComputerSession / SleepSession own
			// the camera while the local player is docked at a station or sleeping in a bed,
			// so skip our write to avoid fighting their zoom lerps.
			if (HasInputAuthority && ComputerSession.IsAnyAtComputer == false && SleepSession.IsAnySleeping == false)
			{
				// Transfer properties from camera handle to Main Camera.
				Camera.main.transform.SetPositionAndRotation(CameraHandle.position, CameraHandle.rotation);

				// Headbob is applied on top of the main camera only — CameraHandle stays untouched
				// so the fire raycast origin (which reads CameraHandle.position) is unaffected.
				Vector3 bob = ComputeHeadBob();
				if (bob != Vector3.zero)
				{
					Camera.main.transform.position += Camera.main.transform.rotation * bob;
				}

				ApplyCameraCollision();

				ApplySprintFOV();
			}
		}

		// Sweep a sphere from CameraPivot to the camera's current position; if anything blocks, clamp the
		// camera onto the safe side of the hit. CameraHandle is offset along the pivot's local +Y, so
		// pitching up swings it forward — combined with the climb stand-off (~0.4 m), the camera can poke
		// through the wall when looking up while anchored. Pivot-origin SphereCast keeps the camera on the
		// player's side of any blocker, regardless of which direction the offset is currently rotated.
		private void ApplyCameraCollision()
		{
			var cam = Camera.main;
			if (cam == null || CameraPivot == null) return;

			Vector3 pivotPos = CameraPivot.position;
			Vector3 desired = cam.transform.position;
			Vector3 offset = desired - pivotPos;
			float desiredDist = offset.magnitude;
			if (desiredDist < 0.0001f) return;

			Vector3 dir = offset / desiredDist;
			float radius = Mathf.Max(0.01f, CameraCollisionRadius);
			// Always exclude the player's own layer — the SphereCast expands outward from the pivot
			// and would otherwise catch on the KCC capsule / nearby head hitboxes sitting on the same layer.
			int mask = CameraCollisionMask & ~(1 << gameObject.layer);

			if (Physics.SphereCast(pivotPos, radius, dir, out RaycastHit hit, desiredDist, mask, QueryTriggerInteraction.Ignore))
			{
				float safeDist = Mathf.Max(0f, hit.distance - CameraCollisionPadding);
				cam.transform.position = pivotPos + dir * safeDist;
			}
		}

		private void ApplySprintFOV()
		{
			var cam = Camera.main;
			if (cam == null) return;

			if (_baseFOV < 0f) _baseFOV = cam.fieldOfView;

			float horizontalSpeed = new Vector2(KCC.RealVelocity.x, KCC.RealVelocity.z).magnitude;
			float sprintT = Mathf.InverseLerp(WalkSpeed, SprintSpeed, horizontalSpeed);
			bool canBoost = Health.IsAlive && RagdollState == ERagdollState.Normal;
			float targetFOV = _baseFOV + (canBoost ? SprintFOVBoost * sprintT : 0f);

			cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, FOVLerpSpeed * Time.deltaTime);
		}

		private Vector3 ComputeHeadBob()
		{
			Vector3 vel = KCC.RealVelocity;
			float horizontalSpeed = new Vector2(vel.x, vel.z).magnitude;
			bool active = KCC.IsGrounded
				&& horizontalSpeed > 0.5f
				&& Health.IsAlive
				&& RagdollState == ERagdollState.Normal;

			float sprintT = Mathf.InverseLerp(WalkSpeed, SprintSpeed, horizontalSpeed);
			float amplitude = Mathf.Lerp(WalkBobAmplitude, SprintBobAmplitude, sprintT);
			float frequency = Mathf.Lerp(WalkBobFrequency, SprintBobFrequency, sprintT);

			_bobWeight = Mathf.MoveTowards(_bobWeight, active ? 1f : 0f, BobBlendSpeed * Time.deltaTime);
			if (_bobWeight <= 0.0001f) return Vector3.zero;

			_bobPhase += frequency * Time.deltaTime * Mathf.PI * 2f;
			// Wrap at 4π — sin(phase) has period 2π, but the horizontal cos(phase * 0.5) below
			// has period 4π. 4π is the LCM so both stay continuous through the wrap; wrapping
			// at 2π would snap cos(phase * 0.5) from -1 to +1 every cycle.
			if (_bobPhase > Mathf.PI * 4f) _bobPhase -= Mathf.PI * 4f;

			float vertical = Mathf.Sin(_bobPhase) * amplitude * _bobWeight;
			float horizontal = Mathf.Cos(_bobPhase * 0.5f) * amplitude * BobHorizontalRatio * _bobWeight;
			return new Vector3(horizontal, vertical, 0f);
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

			// Crouch: hold-to-crouch while grounded. Releasing the button stands back up unless a
			// ceiling sits within CrouchStandClearance — in that case the player stays crouched.
			// Knockback also forces stand-up clear-check anyway; the want-flag is just suppressed.
			bool wantsCrouch = !knockbackActive && input.Buttons.IsSet(EInputButton.Crouch);
			bool wantToBeCrouched = wantsCrouch && KCC.IsGrounded;
			if (IsCrouching && !wantToBeCrouched && HasCeilingAbove())
			{
				wantToBeCrouched = true;
			}
			IsCrouching = wantToBeCrouched;
			ApplyCrouchHeight();

			bool wantsSprint = input.Buttons.IsSet(EInputButton.Sprint);
			bool isMoving = input.MoveDirection.sqrMagnitude > 0.01f;
			// Allow continuing a sprint as long as stamina > 0; require MinStaminaToStartSprint to (re)start.
			float startThreshold = _wasSprinting ? 0f : MinStaminaToStartSprint;
			bool isSprinting = !IsCrouching && !knockbackActive && wantsSprint && isMoving && Stamina > startThreshold;

			float speed;
			if (IsCrouching && KCC.IsGrounded) speed = CrouchSpeed;
			else if (isSprinting) speed = SprintSpeed;
			else speed = WalkSpeed;
			if (_inventory != null) speed *= _inventory.SpeedMultiplier;
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

			bool jumpPressed = !knockbackActive && !IsCrouching && input.Buttons.WasPressed(previousButtons, EInputButton.Jump);

			// Mid-air grab attempt: pressing Jump in the air OR holding forward into a wall while airborne
			// tries to latch onto a climbable surface. The grounded jump-press path below never attempts
			// to grab, so a vertical jump is always preserved when standing next to a wall.
			bool wantsMidAirGrab = !KCC.IsGrounded && (jumpPressed || input.MoveDirection.y > 0.5f);
			if (wantsMidAirGrab && TryEnterClimb())
			{
				// Climb engaged. Cancel any in-progress melee charge — it's nonsensical mid-grab,
				// and we wouldn't process the release anyway (climbing routes to ProcessClimbInput).
				if (ActionInvoker != null && ActionInvoker.IsCharging)
				{
					ActionInvoker.CancelCharge();
				}
				// Bypass the rest of normal movement this tick — TryEnterClimb already snapped
				// the KCC to the anchor; no further MovePlayer call is needed. ProcessClimbInput
				// takes over from the next tick.
				_wasSprinting = false;
				return drained;
			}

			// Comparing current input buttons to previous input buttons - this prevents glitches when input is lost
			if (jumpPressed && KCC.IsGrounded)
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
			// Placeable in hand: LMB is handled locally by PlacementController (which sends
			// the place RPC directly). Suppress weapon-fire logic and cancel any stale charge.
			if (_inventory != null && _inventory.SelectedDefinition is PlaceableDefinition)
			{
				if (ActionInvoker != null && ActionInvoker.IsCharging) ActionInvoker.CancelCharge();
				return;
			}

			// Consumable in hand: LMB press consumes one and applies its effect.
			// Cancel any stale charge so re-equipping a melee weapon can't release a charged swing.
			if (_inventory != null && _inventory.SelectedDefinition is ConsumableDefinition)
			{
				if (ActionInvoker != null && ActionInvoker.IsCharging) ActionInvoker.CancelCharge();
				if (input.Buttons.WasPressed(previousButtons, EInputButton.Fire))
				{
					_inventory.TryUseSelectedConsumable();
				}
				return;
			}

			var action = GetActiveAction(GetActionProvider());
			if (action == null)
			{
				// Nothing to fire with — drop any stale charge so it doesn't bleed into the next held item.
				if (ActionInvoker != null && ActionInvoker.IsCharging) ActionInvoker.CancelCharge();
				return;
			}

			bool wasPressed = input.Buttons.WasPressed(previousButtons, EInputButton.Fire);
			bool wasReleased = input.Buttons.WasReleased(previousButtons, EInputButton.Fire);

			if (action.Charge.Enabled == false)
			{
				if (ActionInvoker.IsCharging) ActionInvoker.CancelCharge();
				if (wasPressed) Fire(action, false);
				return;
			}

			if (wasPressed && ActionInvoker.IsCharging == false && ActionInvoker.CanFire)
			{
				ActionInvoker.StartCharge();
			}
			else if (wasReleased && ActionInvoker.IsCharging)
			{
				if (ActionInvoker.ReleaseCharge(out float chargeSeconds))
				{
					bool charged = chargeSeconds >= action.Charge.ThresholdSeconds;
					Fire(action, charged);
				}
			}
		}

		// BOTW-style climb anchor: cast forward (yaw-only, ignoring pitch so looking up at the wall doesn't reject the grab)
		// from the chest. If a steep enough surface is hit on the ClimbableMask, snap the KCC to the anchor distance and
		// flip IsClimbing on. Caller (ProcessInput) decides whether to suppress the normal jump impulse.
		// State-authority-only via the mutation of [Networked] fields below — proxies see the change replicate.
		private bool TryEnterClimb()
		{
			if (CanClimb == false) return false;
			// Crouch can't survive a wall-grab — the climb basis assumes a normal-height capsule.
			if (IsCrouching) { IsCrouching = false; ApplyCrouchHeight(); }
			// No stamina check here: zero-stamina grabs are intentionally allowed and degrade into a
			// slow slide-down inside ProcessClimbInput. Acts as a soft failsafe — a falling player can
			// still catch the wall, they just won't be able to climb up.

			// Yaw-only forward — pitch is intentionally ignored so the player can look up the wall
			// while still grabbing it horizontally. Matches BOTW behavior.
			Vector3 forwardYaw = KCC.TransformRotation * Vector3.forward;
			forwardYaw.y = 0f;
			if (forwardYaw.sqrMagnitude < 0.0001f) return false;
			forwardYaw.Normalize();

			Vector3 origin = GetClimbProbeOrigin();
			if (Physics.Raycast(origin, forwardYaw, out RaycastHit hit, ClimbWallProbeDistance, ClimbableMask, QueryTriggerInteraction.Ignore) == false)
				return false;

			// Reject too-horizontal surfaces (floors, gentle ramps).
			if (Vector3.Angle(hit.normal, Vector3.up) < MaxClimbSurfaceAngle) return false;

			_climbWallNormal = hit.normal;
			IsClimbing = true;
			_isJumping = false;
			_moveVelocity = Vector3.zero;

			// Snap the player to the standoff distance from the wall so subsequent
			// re-probes hit cleanly. Vertical position is preserved.
			Vector3 anchor = hit.point + hit.normal * ClimbStickRange;
			anchor.y = transform.position.y;
			KCC.SetPosition(anchor);
			// Kill any leftover jump impulse / external velocity from the moment of grabbing so the
			// player stops dead on the wall instead of riding their incoming momentum upward.
			KCC.ResetVelocity();
			return true;
		}

		// Driven by FixedUpdateNetwork while IsClimbing. Returns true if stamina drained this tick
		// (so the regen check in FixedUpdateNetwork stays gated).
		private bool ProcessClimbInput(GameplayInput input, NetworkButtons previousButtons)
		{
			// Player can still look around while on the wall — pitch is preserved for ledge inspection.
			KCC.SetLookRotation(input.LookRotation, -90f, 90f);

			// No gravity while anchored — the wall holds the player.
			KCC.SetGravity(0f);

			// Zero the KCC's internal velocity each tick. KCC.Move only sets the kinematic/desired
			// velocity; any residual ExternalVelocity from a prior jumpImpulse or hop burst would
			// persist forever with gravity=0, causing the player to glide indefinitely. Resetting
			// here makes each tick's KCC.Move call the sole source of motion while climbing.
			KCC.ResetVelocity();

			// Drop button: clean release, no stamina cost, no impulse.
			if (input.Buttons.WasPressed(previousButtons, EInputButton.ClimbDrop))
			{
				ExitClimb();
				MovePlayer(Vector3.zero, 0f);
				return false;
			}

			// Early-mantle: even while still on the wall, if a flat ledge top is within reach above
			// the chest, snap into the mantle now. Lets the player get "dragged up" from below
			// instead of having to climb until the chest fully clears the wall.
			if (TryFindLedgeAbove(out Vector3 earlyMantleEnd))
			{
				_mantleStart = transform.position;
				_mantleEnd = earlyMantleEnd;
				_mantleTimer = TickTimer.CreateFromSeconds(Runner, MantleDuration);
				IsClimbing = false;
				_climbWallNormal = Vector3.zero;
				_moveVelocity = Vector3.zero;
				return true;
			}

			// Re-probe the wall to update the normal (handles convex curves and segmented geometry)
			// and detect when the player has climbed off the side.
			Vector3 probeDir = -_climbWallNormal;
			Vector3 origin = GetClimbProbeOrigin();
			if (Physics.Raycast(origin, probeDir, out RaycastHit hit, ClimbWallProbeDistance + ClimbStickRange, ClimbableMask, QueryTriggerInteraction.Ignore) == false)
			{
				// Lost wall contact. If a flat ledge is right above us, kick off a mantle; otherwise drop.
				if (TryStartMantle(out Vector3 mantleEnd))
				{
					_mantleStart = transform.position;
					_mantleEnd = mantleEnd;
					_mantleTimer = TickTimer.CreateFromSeconds(Runner, MantleDuration);
					IsClimbing = false;
					_climbWallNormal = Vector3.zero;
					_moveVelocity = Vector3.zero;
					return false;
				}
				ExitClimb();
				MovePlayer(Vector3.zero, 0f);
				return false;
			}
			_climbWallNormal = hit.normal;

			// Wall-surface fallback basis: horizontal along wall, vertical along wall. Used when the
			// camera is looking straight at (or directly away from) the wall and the projected look
			// direction degenerates to zero. Cross order is (wallNormal, up) — produces the player's
			// real right (not their left) when facing INTO the wall (i.e. opposite the wall normal).
			Vector3 surfaceRight = Vector3.Cross(_climbWallNormal, Vector3.up);
			if (surfaceRight.sqrMagnitude < 0.0001f)
			{
				// Wall normal is nearly vertical (i.e. ceiling/floor) — shouldn't happen given the
				// entry angle check, but bail rather than divide by zero on a degenerate basis.
				ExitClimb();
				MovePlayer(Vector3.zero, 0f);
				return false;
			}
			surfaceRight.Normalize();
			Vector3 surfaceUp = Vector3.Cross(surfaceRight, _climbWallNormal).normalized;

			// View-relative basis: project the camera look direction onto the wall plane and use
			// that as the "forward" (W) axis. So looking up at the wall and pressing W climbs up,
			// looking sideways and pressing W moves sideways. Falls back to surfaceUp when the player
			// is looking directly at or directly away from the wall (projection collapses to zero).
			float pitch = Mathf.Clamp(input.LookRotation.x, -90f, 90f);
			float yaw = input.LookRotation.y;
			Vector3 lookDir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
			Vector3 wallForward = lookDir - Vector3.Dot(lookDir, _climbWallNormal) * _climbWallNormal;
			Vector3 up;
			Vector3 right;
			if (wallForward.sqrMagnitude < 0.01f)
			{
				up = surfaceUp;
				right = surfaceRight;
			}
			else
			{
				up = wallForward.normalized;
				right = Vector3.Cross(_climbWallNormal, up).normalized;
			}

			bool isMoving = input.MoveDirection.sqrMagnitude > 0.01f;
			Vector3 wallVel = (right * input.MoveDirection.x + up * input.MoveDirection.y) * ClimbSpeed;

			// Out-of-stamina slide: stay attached but override player input with a slow downward slide
			// along the wall. Climb-hop and wall-leap below already gate on Stamina > ClimbStaminaJumpCost,
			// so they're naturally disabled. Slide is along surfaceUp (wall-plane up) — using Vector3.down
			// would push into the wall on overhanging surfaces. After ClimbDepletedHoldDuration the
			// player lets go and falls. Timer is started on the first tick stamina is 0 and cleared
			// when stamina comes back, so a brief food/regen window resets the grace period.
			bool outOfStamina = Stamina <= 0f;
			if (outOfStamina)
			{
				if (_climbSlideTimer.IsRunning == false)
				{
					_climbSlideTimer = TickTimer.CreateFromSeconds(Runner, ClimbDepletedHoldDuration);
				}
				else if (_climbSlideTimer.Expired(Runner))
				{
					ExitClimb();
					MovePlayer(Vector3.zero, 0f);
					return false;
				}

				wallVel = -surfaceUp * ClimbDepletedSlideSpeed;
				isMoving = false;
			}
			else
			{
				_climbSlideTimer = default;
			}

			// Wall-leap: when looking away from the wall, Jump fires a one-shot launch in the look
			// direction and exits climb so air physics carries the arc to a neighboring wall. Without
			// this, the in-wall climb-hop path treats world-up as "forward" whenever the look direction
			// projects to zero on the wall plane, so pressing W+Jump while facing outward sends the
			// player straight up the current wall instead of away from it. Dot threshold 0.2 ≈ 78° from
			// the wall normal — clearly facing outward, not merely glancing sideways along the wall.
			if (input.Buttons.WasPressed(previousButtons, EInputButton.Jump)
				&& Vector3.Dot(lookDir, _climbWallNormal) > 0.2f
				&& Stamina > ClimbStaminaJumpCost)
			{
				Stamina = Mathf.Max(0f, Stamina - ClimbStaminaJumpCost);
				_staminaRegenTimer = TickTimer.CreateFromSeconds(Runner, StaminaRegenDelay);

				Vector3 launchHoriz = new Vector3(lookDir.x, 0f, lookDir.z);
				if (launchHoriz.sqrMagnitude > 0.0001f) launchHoriz.Normalize();
				else launchHoriz = new Vector3(_climbWallNormal.x, 0f, _climbWallNormal.z).normalized;

				Vector3 launchVel = launchHoriz * WallLeapHorizontalSpeed;

				ExitClimb();
				_moveVelocity = launchVel;
				_isJumping = true;
				// Restore gravity before the Move so the upward impulse starts decelerating immediately;
				// the climb path zeroed gravity earlier this tick.
				KCC.SetGravity(UpGravity);
				KCC.Move(launchVel, WallLeapJumpImpulse);
				return true;
			}

			// Climb-hop trigger: Jump press while the cooldown timer is expired and stamina covers the cost.
			// Stays in IsClimbing; the upward burst phase below adds vertical velocity while the timer is fresh.
			if (input.Buttons.WasPressed(previousButtons, EInputButton.Jump)
				&& _climbJumpTimer.ExpiredOrNotRunning(Runner)
				&& Stamina > ClimbStaminaJumpCost)
			{
				Stamina = Mathf.Max(0f, Stamina - ClimbStaminaJumpCost);
				_climbJumpTimer = TickTimer.CreateFromSeconds(Runner, ClimbJumpCooldown);
			}

			// Burst phase: while the timer is in its first ClimbHopBurstDuration seconds, add a sustained
			// hop velocity. WASD picks the direction along the wall (hop along surface); no movement
			// input means "jump off" — push back along the wall normal and up. Using the timer's remaining
			// time as the gate shares one networked timer between motion and cooldown.
			float? hopRemaining = _climbJumpTimer.RemainingTime(Runner);
			bool inHopBurst = hopRemaining.HasValue && hopRemaining.Value > ClimbJumpCooldown - ClimbHopBurstDuration;
			if (inHopBurst)
			{
				Vector3 hopDir;
				if (isMoving)
				{
					// Hop along the wall surface, matching the WASD direction (W = up the wall, S = down, A/D = sideways).
					hopDir = (right * input.MoveDirection.x + up * input.MoveDirection.y).normalized;
				}
				else
				{
					// No directional input — jump OFF the wall. Mix wall-outward with world-up for a back-flip arc.
					hopDir = (_climbWallNormal + Vector3.up).normalized;
				}
				wallVel += hopDir * ClimbJumpImpulse;
			}

			// Stamina drain: idle drain still ticks (BOTW does this), move drain is the dominant cost.
			float drainRate = isMoving ? ClimbStaminaMoveDrain : ClimbStaminaIdleDrain;
			Stamina = Mathf.Max(0f, Stamina - drainRate * Runner.DeltaTime);
			_staminaRegenTimer = TickTimer.CreateFromSeconds(Runner, StaminaRegenDelay);

			// At zero stamina the player is sliding down. When the slide reaches the ground, release
			// so they can walk away normally instead of hanging with feet planted on the floor and
			// hands still on the wall. Stamina won't regenerate while IsClimbing (the drain block
			// above resets _staminaRegenTimer every tick), so the only way out is grounded-release,
			// ClimbDrop, or sliding past the bottom of the wall (handled by the re-probe miss path).
			if (outOfStamina && KCC.IsGrounded)
			{
				ExitClimb();
				MovePlayer(Vector3.zero, 0f);
				return false;
			}

			// Anchor correction along the wall normal: blend the player back to the standoff distance.
			// Converting the position delta to velocity (delta / dt) lets us fold it into KCC.Move,
			// which keeps SimpleKCC's collision resolution in the loop instead of teleporting.
			// Skipped during a hop burst — otherwise the outward push above would be immediately cancelled.
			Vector3 totalVel = wallVel;
			if (inHopBurst == false)
			{
				Vector3 anchorTarget = hit.point + _climbWallNormal * ClimbStickRange;
				Vector3 delta = anchorTarget - transform.position;
				Vector3 normalCorrection = Vector3.Project(delta, _climbWallNormal);
				totalVel += normalCorrection / Runner.DeltaTime;
			}

			_moveVelocity = wallVel;
			KCC.Move(totalVel, 0f);

			return true;
		}

		private void ExitClimb()
		{
			IsClimbing = false;
			_climbWallNormal = Vector3.zero;
			_climbJumpTimer = default;
			_climbSlideTimer = default;
			_moveVelocity = Vector3.zero;
		}

		// Per-tick scan run while still anchored on the wall. If the wall ends within LedgeReachDistance
		// above the chest AND there's a flat top out beyond the wall normal, returns the mantle end
		// position so the player can be pulled up onto the ledge from below.
		private bool TryFindLedgeAbove(out Vector3 endPos)
		{
			endPos = Vector3.zero;
			if (LedgeReachDistance <= 0f) return false;

			Vector3 intoWall = -_climbWallNormal;
			intoWall.y = 0f;
			if (intoWall.sqrMagnitude < 0.0001f) return false;
			intoWall.Normalize();

			// Confirm the wall ends within reach above the chest: cast forward at chest+reach and
			// require it to miss any climbable surface. If the wall still extends up there, we're
			// not at a ledge yet — bail.
			Vector3 chestOrigin = GetClimbProbeOrigin();
			Vector3 upperProbeOrigin = chestOrigin + Vector3.up * LedgeReachDistance;
			float forwardRange = ClimbWallProbeDistance + ClimbStickRange;
			if (Physics.Raycast(upperProbeOrigin, intoWall, forwardRange, ClimbableMask, QueryTriggerInteraction.Ignore))
				return false;

			// Find the flat top: cast down from above-and-forward of the chest. Range covers the
			// reach window plus a little slack so we still catch tops that are level with chest+reach.
			Vector3 castOrigin = chestOrigin
				+ Vector3.up * (LedgeReachDistance + 0.3f)
				+ intoWall * MantleForwardDistance;
			float castDistance = LedgeReachDistance + 0.6f;
			if (Physics.Raycast(castOrigin, Vector3.down, out RaycastHit hit, castDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) == false)
				return false;

			if (Vector3.Angle(hit.normal, Vector3.up) > 30f) return false;

			endPos = hit.point + Vector3.up * 0.05f;
			return true;
		}

		// Called when the wall re-probe misses during ProcessClimbInput. Looks for a roughly flat surface
		// just above-and-forward of the player (i.e. the top of the wall they just climbed off of). Returns
		// true with the final mantle position if a ledge is found. Uses DefaultRaycastLayers so the ledge
		// top doesn't have to be on the Climbable layer — it just has to be flat.
		private bool TryStartMantle(out Vector3 endPos)
		{
			endPos = Vector3.zero;

			// Use the wall normal we had last to determine "forward" — pointing into the wall. This
			// is more reliable than KCC.TransformRotation in cases where the player was facing sideways.
			Vector3 intoWall = -_climbWallNormal;
			intoWall.y = 0f;
			if (intoWall.sqrMagnitude < 0.0001f) return false;
			intoWall.Normalize();

			// Cast straight down from a point above-and-forward of the player. The downward cast looks
			// for the top surface of whatever they were climbing.
			Vector3 castOrigin = transform.position
				+ Vector3.up * (MantleUpDistance + 0.3f)
				+ intoWall * MantleForwardDistance;
			float castDistance = MantleUpDistance + 0.6f;

			if (Physics.Raycast(castOrigin, Vector3.down, out RaycastHit hit, castDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) == false)
				return false;

			// Reject non-flat tops (e.g. another wall, a steep ramp). 30° tolerance covers minor slopes.
			if (Vector3.Angle(hit.normal, Vector3.up) > 30f) return false;

			// Small lift so the KCC doesn't end embedded in the surface.
			endPos = hit.point + Vector3.up * 0.05f;
			return true;
		}

		// Runs while _mantleTimer is active. Lerps the KCC from _mantleStart to _mantleEnd with smooth-step
		// easing, then clears the timer so normal control resumes the next tick.
		private void ProcessMantle()
		{
			KCC.SetGravity(0f);

			float? remaining = _mantleTimer.RemainingTime(Runner);
			if (remaining.HasValue == false || remaining.Value <= 0f)
			{
				// Land the player exactly on the end position and clear mantle state.
				KCC.SetPosition(_mantleEnd);
				_mantleTimer = default;
				_mantleStart = Vector3.zero;
				_mantleEnd = Vector3.zero;
				_moveVelocity = Vector3.zero;
				return;
			}

			float t = MantleDuration > 0f ? 1f - Mathf.Clamp01(remaining.Value / MantleDuration) : 1f;
			float eased = Mathf.SmoothStep(0f, 1f, t);
			KCC.SetPosition(Vector3.Lerp(_mantleStart, _mantleEnd, eased));
		}

		// Origin used for both entry and re-probe raycasts. Using the chest bone keeps the probe at the
		// same in-world position the body occupies on every peer, so authority and proxies probe identically.
		// Falls back to a fixed offset above the root when no ChestBone is wired.
		private Vector3 GetClimbProbeOrigin()
		{
			return ChestBone != null ? ChestBone.position : transform.position + Vector3.up * 1.0f;
		}

		// Drives the KCC capsule height from the networked IsCrouching state. Called from every path
		// that touches IsCrouching so each peer's capsule matches the replicated state without an OnChangedRender.
		private void ApplyCrouchHeight()
		{
			if (_standHeight <= 0f) return;
			float target = IsCrouching ? CrouchHeight : _standHeight;
			if (Mathf.Abs(KCC.Settings.Height - target) > 0.001f)
			{
				KCC.SetHeight(target);
			}
		}

		// Sphere-cast straight up from the KCC root to detect a ceiling that would prevent standing.
		// Excludes the player's own layer so the cast can't self-hit.
		private bool HasCeilingAbove()
		{
			if (CrouchStandClearance <= 0f) return false;
			Vector3 origin = transform.position + Vector3.up * Mathf.Max(0.1f, CrouchHeight * 0.5f);
			float castDistance = Mathf.Max(0f, CrouchStandClearance - CrouchHeight * 0.5f);
			if (castDistance <= 0f) return false;
			int mask = Physics.DefaultRaycastLayers & ~(1 << gameObject.layer);
			return Physics.SphereCast(origin, CrouchStandCheckRadius, Vector3.up, out _, castDistance, mask, QueryTriggerInteraction.Ignore);
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

		private void Fire(CombatAction action, bool charged)
		{
			if (action == null || ActionInvoker == null) return;

			// Clear hit position in case nothing will be hit
			_hitPosition = Vector3.zero;
			_hitNormal = Vector3.zero;

			var ctx = new ActorContext
			{
				Runner = Runner,
				IgnoreAuthority = Object.InputAuthority,
				AttackerPosition = transform.position,
				FireTransform = CameraHandle,
				AttackerRoot = gameObject,
			};

			var hit = ActionInvoker.TryFire(action, in ctx, charged);
			if (hit.DidFire == false) return;

			if (hit.DidHit)
			{
				_hitPosition = hit.Point;
				_hitNormal = hit.Normal;

				if (hit.KilledTarget && hit.Target != null)
				{
					// Killing chicken grants 1 point, killing other player has -10 points penalty.
					ChickenKills += hit.Target.GetComponent<Chicken>() != null ? 1 : -10;
				}
			}

			// Drives ShowFireEffects on every peer. Counter pattern (not RPC) tolerates dropped ticks.
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
				var provider = GetActionProvider();
				var action = GetActiveAction(provider);

				if (provider != null && action != null)
				{
					provider.PlayAttackSound(action);
					if (action.Style == EFeedbackStyle.Melee)
					{
						provider.PlayMeleeFeedback(ActionInvoker != null && ActionInvoker.LastFireWasCharged);
					}
					else
					{
						provider.PlayRangedFeedback();
					}
				}

				Animator.SetTrigger(_animIDShoot);

				if (_hitPosition != Vector3.zero && ImpactPrefab != null)
				{
					// Impact gets destroyed automatically with DestroyAfter script
					Instantiate(ImpactPrefab, _hitPosition, Quaternion.LookRotation(_hitNormal));
				}
			}

			_visibleFireCount = _fireCount;
		}

		private IActionProvider GetActionProvider()
		{
			if (_inventory == null) return null;
			var instance = _inventory.HeldInstance;
			return instance != null ? instance.GetComponent<IActionProvider>() : null;
		}

		private static CombatAction GetActiveAction(IActionProvider provider)
		{
			if (provider == null || provider.Actions == null || provider.Actions.Count == 0) return null;
			return provider.Actions[0];
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

		private void OnCanClimbChanged()
		{
			// Hook for HUD / future feedback. State-side gating is read from CanClimb directly.
		}

		private void OnIsClimbingChanged()
		{
			// Held-weapon suppression is driven from Render() every frame to avoid timing races.
			// This hook is kept for future per-peer SFX/animator triggers tied to the climb edge.
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

		// ---------------- Sleep state ----------------

		/// <summary>State-authority-only: flip the sleep flag and clean up any incompatible activity. Called by <see cref="Bed.RPC_RequestSleep"/> / <see cref="Bed.HostReleaseOccupant"/>.</summary>
		public void AuthoritySetSleeping(bool sleeping)
		{
			if (HasStateAuthority == false) return;
			if (IsSleeping == sleeping) return;

			IsSleeping = sleeping;

			if (sleeping)
			{
				// Cancel anything that doesn't survive lying down.
				if (IsClimbing) ExitClimb();
				if (ActionInvoker != null && ActionInvoker.IsCharging) ActionInvoker.CancelCharge();
				if (IsCrouching) { IsCrouching = false; ApplyCrouchHeight(); }
				_moveVelocity = Vector3.zero;
				_isJumping = false;
			}
		}

		private void OnSleepingChanged()
		{
			// SleepSession on the input authority listens to this via its own poll of Player.IsSleeping
			// to drive the camera zoom in/out. Nothing extra to do here yet — kept as a hook for VFX/SFX.
		}

		// ---------------- Vehicle / seated state ----------------

		/// <summary>State-authority-only: place this player into the given seat. Called by <see cref="Seat.RPC_RequestEnter"/>.</summary>
		public void HostEnterSeat(Seat seat)
		{
			if (HasStateAuthority == false) return;
			if (seat == null) return;

			InCurrentSeat = seat;

			// Cancel any in-progress activity that doesn't survive entering a vehicle.
			if (IsClimbing) ExitClimb();
			if (ActionInvoker != null && ActionInvoker.IsCharging) ActionInvoker.CancelCharge();
			if (IsCrouching) { IsCrouching = false; ApplyCrouchHeight(); }
			_moveVelocity = Vector3.zero;
			_isJumping = false;

			// Disable the KCC capsule so it doesn't fight the truck collider. Snap to the seat anchor.
			var anchor = seat.Anchor;
			KCC.SetPosition(anchor.position);
			KCC.SetLookRotation(0f, anchor.eulerAngles.y);
			KCC.SetActive(false);
		}

		/// <summary>State-authority-only: take this player out of their seat at the given exit point. Called by <see cref="Seat.RPC_RequestExit"/>.</summary>
		public void HostExitSeat(Vector3 exitPosition)
		{
			if (HasStateAuthority == false) return;

			InCurrentSeat = null;
			KCC.SetActive(true);
			KCC.SetPosition(exitPosition);
		}

		/// <summary>Per-tick while seated (host side): keep the KCC parked on the seat anchor and body yawed with the vehicle.</summary>
		private void UpdateSeated()
		{
			if (HasStateAuthority == false) return;
			var seat = InCurrentSeat;
			if (seat == null) return;

			var anchor = seat.Anchor;
			KCC.SetPosition(anchor.position);
			// Body yaw follows the vehicle. Camera pitch/yaw is applied to CameraPivot in SeatedLateUpdate,
			// so the player can still freely look around without rotating the body off the seat.
			KCC.SetLookRotation(0f, anchor.eulerAngles.y);
		}

		/// <summary>Per-frame visual sync on every peer while seated: snap the player visual onto the seat anchor and suppress weapon/footstep visuals.</summary>
		private void SeatedRender()
		{
			var seat = InCurrentSeat;
			if (seat == null) return;

			var anchor = seat.Anchor;
			// Hard-snap visual to seat each frame so the player rides perfectly even if the
			// vehicle's NetworkTransform interpolation hasn't quite caught up yet.
			transform.position = anchor.position;
			transform.rotation = Quaternion.Euler(0f, anchor.eulerAngles.y, 0f);

			if (_inventory != null && _inventory.SuppressHeldVisual == false)
			{
				_inventory.SuppressHeldVisual = true;
			}

			if (FootstepSound != null) FootstepSound.enabled = false;
			if (DustParticles != null)
			{
				var emission = DustParticles.emission;
				emission.enabled = false;
			}

			// Body animator: clamp to idle-ish values so the run cycle doesn't play while seated.
			Animator.SetFloat(_animIDSpeedX, 0f, 0.1f, Time.deltaTime);
			Animator.SetFloat(_animIDSpeedZ, 0f, 0.1f, Time.deltaTime);
			Animator.SetBool(_animIDGrounded, true);
		}

		/// <summary>Camera follow for the local seated player: drive CameraPivot from raw input look so the camera can pan independently of the body (which is locked to the vehicle).</summary>
		private void SeatedLateUpdate()
		{
			if (HasInputAuthority == false) return;
			if (Input == null) return;

			Vector2 look = Input.LookRotation;
			float bodyYaw = transform.eulerAngles.y;
			float yawRel = Mathf.Clamp(Mathf.DeltaAngle(bodyYaw, look.y), -100f, 100f);
			float pitch = Mathf.Clamp(look.x, -75f, 75f);
			CameraPivot.localRotation = Quaternion.Euler(pitch, yawRel, 0f);

			if (Camera.main != null && CameraHandle != null)
			{
				Camera.main.transform.SetPositionAndRotation(CameraHandle.position, CameraHandle.rotation);
				// Skip ApplyCameraCollision while seated — the truck's chassis/cab colliders would
				// snap the camera onto the cab interior wall whenever the player looks sideways.
			}
		}

		private void OnInCurrentSeatChanged()
		{
			// Local-side toggles. KCC active/inactive is driven by Host* helpers on the state
			// authority side, but proxies/input-authority must mirror so collisions match.
			if (KCC != null)
			{
				bool seated = IsSeated;
				if (seated)
				{
					KCC.SetActive(false);
				}
				else
				{
					// Reactivate the capsule on exit. Position is already set by the host's SetPosition,
					// which has replicated by the time this hook fires.
					KCC.SetActive(true);
				}
			}

			if (_inventory != null)
			{
				_inventory.SuppressHeldVisual = IsSeated;
			}
		}
	}
}
