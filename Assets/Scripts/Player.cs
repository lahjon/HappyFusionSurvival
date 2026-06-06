using UnityEngine;
using Fusion;
using Fusion.Addons.SimpleKCC;
using Sirenix.OdinInspector;
using Starter.Common.Interactions;
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
	public sealed class Player : NetworkBehaviour, IKnockbackable, IInteractable, IHoldInteractable, IInteractionGate
	{
		[Header("References")]
		public Health Health;
		public SimpleKCC KCC;
		public PlayerInput Input;
		public Animator Animator;
		[Tooltip("Full body animator — visible to other players only.")]
		public Animator BodyAnimator;
		[Tooltip("No-head body animator — visible to local player only.")]
		public Animator NoHeadAnimator;
		[Tooltip("Debug: show both FullBody and NoHead for the local player so both are visible in the scene view.")]
		public bool ShowBothBodiesDebug = false;

		[Header("Arm IK")]
		[Tooltip("Enable IK on the right hand — move RightHandTarget to the item grip point.")]
		public bool UseRightArmIK = false;
		[Tooltip("Enable IK on the left hand — move LeftHandTarget to the off-hand grip point.")]
		public bool UseLeftArmIK = false;
		[Tooltip("When on, arm IK only applies to PlayerFullBody (other players' view) and not PlayerNoHead (local view).")]
		public bool UseIKOnlyOnFullBody = false;
		[Tooltip("Anchor under which the held item visual lives at runtime. Children are hidden during emotes.")]
		public Transform HandItemAnchor;
		[Tooltip("Right hand IK target — move this to where the held item's grip is.")]
		public Transform RightHandTarget;
		[Tooltip("Left hand IK target — move this to where the off-hand grip is.")]
		public Transform LeftHandTarget;
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

		[Header("Fall Damage")]
		[Tooltip("Master toggle for landing/fall damage.")]
		public bool EnableFallDamage = true;
		[Tooltip("Downward impact speed (m/s) at or below which a landing deals no damage. Keep this above the speed a normal jump lands at (~13 m/s with the default jump/gravity) so ordinary hops never hurt.")]
		public float SafeFallSpeed = 16f;
		[Tooltip("Downward impact speed (m/s) at or above which a landing deals the full MaxFallDamage. Damage scales linearly between SafeFallSpeed and this.")]
		public float LethalFallSpeed = 32f;
		[Tooltip("Damage dealt by a landing at or above LethalFallSpeed. Set to the player's max health for a guaranteed kill on a long drop.")]
		public int MaxFallDamage = 10;

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
		[Tooltip("Stamina drained per second while steadying a scoped weapon (holding Sprint while aiming). High by design — a held breath is short. When it empties, the scope jolts (see AimTuning.BreathDepletionRecoilScale).")]
		public float BreathHoldStaminaDrainPerSecond = 45f;

		[Header("Money")]
		[Tooltip("Money granted to the player on spawn. Seeded once on state authority in Spawned(); not re-applied on respawn (Money survives death).")]
		public int StartingMoney = 30;

		[Header("Scraps")]
		[Tooltip("Scraps (generic crafting currency) granted on spawn. Seeded once on state authority in Spawned(); not re-applied on respawn (Scraps survive death). Earned by scavenging items at a crafting bench.")]
		public int StartingScraps = 0;

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
		[Tooltip("Minimum surface angle (degrees from horizontal) for a deliberate mid-air grab to latch a wall. Below this it's treated as a floor/ramp and rejected.")]
		public float MaxClimbSurfaceAngle = 80f;
		[Tooltip("Grounded: pushing forward into a climbable surface steeper than this (degrees from horizontal) is too steep to walk and auto-engages a climb. Set just above the KCC's max walkable ground angle so gentle ramps stay walkable and only steep faces force a climb.")]
		public float ClimbForceAngle = 55f;
		[Tooltip("Climb stamina drain multiplier at a vertical (90°) wall. Drain scales from 1x at ClimbForceAngle up to this at vertical, so steeper surfaces cost more stamina. 1 = no angle scaling.")]
		public float ClimbSteepStaminaMultiplier = 2f;
		[Tooltip("Duration of the mantle animation onto a ledge.")]
		public float MantleDuration = 0.6f;
		[Tooltip("How far forward (beyond the wall normal) the mantle ends.")]
		public float MantleForwardDistance = 0.7f;
		[Tooltip("How far up the mantle lifts the player.")]
		public float MantleUpDistance = 1.1f;
		[Tooltip("While climbing, if a flat ledge top is within this distance above the chest, the mantle triggers early instead of waiting for the chest to clear the wall. Higher = grabs onto ledges from further below.")]
		public float LedgeReachDistance = 0.8f;
		[Tooltip("Grace window (seconds) after the wall probe loses contact before the player drops. Gives them time to physically round a sharp corner where the wall briefly isn't in front of the chest. The player coasts with their climb intent during this window.")]
		public float ClimbCornerGraceDuration = 0.25f;
		[Tooltip("After the corner search adopts a new face, the wall normal is locked to it for this long (seconds) so it can't flip back to the previous face. Prevents the apex 'flicker'/freeze when two faces meet at a sharp corner. Should be long enough to physically clear the corner apex (~0.3s).")]
		public float ClimbWrapCommitDuration = 0.3f;
		[Tooltip("DEBUG: draw climb wall/corner probes in the Scene view and log probe outcomes. Turn off for release.")]
		public bool DebugClimbProbes = false;

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

		[Header("Soft player push")]
		[Tooltip("Players (and bots) within this radius gently push each other apart so bodies can't hard-block doorways / entrances. 0 disables.")]
		[Min(0f)] public float PlayerPushRadius = 1f;
		[Tooltip("Max separation speed (m/s) at full overlap. Keep small — this is a nudge to clear bodies, not knockback.")]
		[Min(0f)] public float PlayerPushStrength = 2.5f;

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

		[Header("Downed / Revive")]
		[Tooltip("Seconds the player bleeds out after being knocked down. The timer pauses while at least one ally is reviving.")]
		public float DownedBleedOutSeconds = 20f;
		[Tooltip("Seconds an ally must hold Interact to fully revive a downed player.")]
		public float ReviveHoldSeconds = 3f;
		[Tooltip("Horizontal crawl speed while downed (m/s). Replaces Walk/Sprint/Crouch speed.")]
		public float DownedCrawlSpeed = 0.8f;
		[Tooltip("KCC capsule height while downed (meters). Should be lower than CrouchHeight so the body reads as on the floor.")]
		public float DownedHeight = 0.55f;
		[Tooltip("Local-space vertical offset added to CameraPivot while downed (negative = lower). Stacks with CrouchCameraDrop semantics — this is the absolute drop, not additive.")]
		public float DownedCameraDrop = -1.05f;
		[Tooltip("HP restored when an ally completes a revive. Capped at Health.InitialHealth.")]
		public int ReviveHealth = 1;
		[Tooltip("Maximum distance from the downed player at which a revive is accepted. Host re-validates with a 1.25x slack.")]
		public float ReviveInteractRange = 1.6f;
		[Tooltip("Text shown in the interaction HUD when this player is downed and can be revived.")]
		public string ReviveInteractLabel = "Revive";

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
		[Tooltip("How quickly the camera eases along the pivot→camera axis when the collision clamp engages or releases. Higher = snappier; too high reintroduces the pop near walls.")]
		public float CameraCollisionLerpSpeed = 16f;

		[Networked, HideInInspector, Capacity(24), OnChangedRender(nameof(OnNicknameChanged))]
		public string Nickname { get; set; }
		/// <summary>Soft currency. Survives respawn — death does not reset money. Granted by money pickups (auto-collected on proximity) and any future loot/sell/quest hook via <see cref="AddMoney"/>.</summary>
		[Networked, HideInInspector]
		public int Money { get; private set; }
		/// <summary>Generic crafting currency. Survives respawn — death does not reset scraps. Earned by scavenging items at a crafting bench (<see cref="AddScraps"/>); spent on recipes with a ScrapCost (<see cref="TrySpendScraps"/>).</summary>
		[Networked, HideInInspector]
		public int Scraps { get; private set; }
		[Networked, HideInInspector]
		public float Stamina { get; private set; }
		/// <summary>True while the player is crouched. Replicated so proxies shrink the capsule and mirror posture.</summary>
		[Networked, HideInInspector]
		public NetworkBool IsCrouching { get; private set; }

		/// <summary>Stable actor identity, decoupled from Fusion input authority. Equals <see cref="NetworkObject.InputAuthority"/>
		/// for human players; for AI bots (no connection) it is a synthetic ref allocated by <see cref="GameManager"/>
		/// (see <see cref="ConfigureAsBot"/>). Team assignment, the win scan, and combat identity key on THIS, not
		/// InputAuthority — so bots are first-class match participants. Written once on the authority.</summary>
		[Networked, HideInInspector]
		public PlayerRef Owner { get; private set; }

		/// <summary>True if this Player is an AI bot driven by <see cref="BotBrain"/> rather than a connected client.
		/// Set once in <see cref="ConfigureAsBot"/> before Spawned; drives the input seam and skips nickname RPCs.</summary>
		[Networked, HideInInspector]
		public NetworkBool IsBot { get; private set; }

		[Networked]
		private Vector3 _moveVelocity { get; set; }
		[Networked, OnChangedRender(nameof(OnJumpingChanged))]
		private NetworkBool _isJumping { get; set; }
		[Networked]
		private NetworkBool _wasSprinting { get; set; }
		// True while a sprint-jump is in flight: latched at takeoff if the player was sprinting,
		// cleared on landing. Lets us keep SprintSpeed in the air even if Sprint is released mid-jump.
		[Networked]
		private NetworkBool _airSprintCarry { get; set; }
		[Networked]
		private TickTimer _staminaRegenTimer { get; set; }
		// Fastest downward speed (m/s, positive while descending) seen during the current fall. Reset on landing.
		[Networked]
		private float _fallPeakDownSpeed { get; set; }
		// KCC grounded state at the end of the previous fall-damage tick, so we can detect the airborne→grounded edge.
		[Networked]
		private NetworkBool _wasGroundedForFall { get; set; }

		/// <summary>The seat the player is currently in (driver or passenger), or null if on foot. Set by <see cref="Seat.RPC_RequestEnter"/> on state authority.</summary>
		[Networked, OnChangedRender(nameof(OnInCurrentSeatChanged))]
		public Seat InCurrentSeat { get; set; }

		/// <summary>True while occupying any seat. Drives input gating, capsule deactivation, and the seated render/camera path.</summary>
		public bool IsSeated => InCurrentSeat != null;

		/// <summary>True while the player is sleeping in a Bed. Sleep is local-camera-only — the body stays in place but movement, input, and damage are suppressed; the bed's <c>SleepSession</c> owns the camera.</summary>
		[Networked, OnChangedRender(nameof(OnSleepingChanged))]
		public NetworkBool IsSleeping { get; private set; }

		/// <summary>True while the player is downed (bleeding out, not yet dead). Set by <see cref="OnLethalDamage"/> via <see cref="Health.AuthorityDownHook"/>; cleared by a successful revive or by the bleed-out timer expiring (which triggers real death).</summary>
		[Networked, OnChangedRender(nameof(OnIsDownedChanged))]
		public NetworkBool IsDowned { get; private set; }

		/// <summary>Yaw (degrees) the player should be looking toward on (re)spawn — taken from the spawn point's facing
		/// (the SpawnPoint arrow direction). Set by the state authority in <see cref="Respawn"/>; the input authority
		/// reacts to <see cref="SpawnLookVersion"/> changes and seeds its accumulated look input so the spawn actually
		/// faces this way instead of the input pipeline snapping back to world +Z on the first tick.</summary>
		[Networked]
		public float SpawnLookYaw { get; private set; }

		/// <summary>Bumped on every (re)spawn so <see cref="OnSpawnLookChanged"/> fires even when two spawns share the same yaw.</summary>
		[Networked, OnChangedRender(nameof(OnSpawnLookChanged))]
		private int SpawnLookVersion { get; set; }

		/// <summary>Seconds remaining on the bleed-out countdown while <see cref="IsDowned"/>. Counted down on state authority each tick when <see cref="ReviveCount"/> is zero — when a reviver is active the timer pauses. Hits 0 → real death.</summary>
		[Networked]
		public float DownedTimeRemaining { get; private set; }

		/// <summary>Number of allies currently holding revive on this downed player. Incremented on <see cref="RPC_BeginRevive"/>, decremented on <see cref="RPC_EndRevive"/>. While &gt; 0 the bleed-out timer pauses and <see cref="ReviveProgress"/> advances.</summary>
		[Networked]
		public int ReviveCount { get; private set; }

		/// <summary>0..1 progress toward a successful revive, accrued on state authority while <see cref="ReviveCount"/> &gt; 0. Resets when the last reviver releases. Reaching 1 fires the actual revive on the next tick.</summary>
		[Networked]
		public float ReviveProgress { get; private set; }

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
		// Starts when the wall probe (straight + corner-wrap) loses contact. While it ticks the player
		// stays attached and coasts so they can round a sharp corner; if it expires without reacquiring
		// a wall they drop. Reset to default the moment any probe finds a wall again.
		[Networked]
		private TickTimer _climbGraceTimer { get; set; }
		// Starts when the corner-wrap search adopts a NEW face. While it ticks, the wrap search is
		// suppressed so the wall normal can't flip back to the previous face — this stops the apex
		// ping-pong (normal oscillating between the two faces) that froze the player and flickered the
		// body rotation. The straight probe along the committed normal still keeps the player anchored.
		[Networked]
		private TickTimer _climbWrapCommitTimer { get; set; }
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
		// Secondary (RMB) attack feedback counter — mirrors _fireCount for the throw swing.
		[Networked]
		private int _secondaryFireCount { get; set; }
		/// <summary>Incremented each time an emote is triggered. OnChangedRender fires SetTrigger on all clients.</summary>
		[Networked, OnChangedRender(nameof(OnEmoteCountChanged))]
		private int _emoteCount { get; set; }
		[Networked]
		private byte _emoteIndex { get; set; }
		/// <summary>True while the player holds the aim (ADS) stance with an Aim-mode weapon. Drives
		/// FOV zoom (local), sway/recoil steadying (PlayerInput), and hitscan spread tightening.</summary>
		[Networked]
		public NetworkBool IsAiming { get; private set; }
		/// <summary>True once this player has entered their team's zone during Night and may fire (arm-once —
		/// stays true for the rest of the night). Written by <see cref="ZoneManager"/> on the state authority via
		/// <see cref="SetPvpArmed"/>; read by the local fire gate (<see cref="CanFireWeaponNow"/>) and UI.</summary>
		[Networked]
		public NetworkBool PvpArmed { get; private set; }
		[Networked]
		private Vector3 _knockbackDirection { get; set; }
		[Networked]
		private float _knockbackInitialSpeed { get; set; }
		[Networked]
		private float _knockbackDuration { get; set; }
		[Networked]
		private TickTimer _knockbackTimer { get; set; }

		// --- Grapple hook (gadget) ---
		/// <summary>True while the grapple is reeling the player toward <see cref="GrappleAnchor"/>. Networked so
		/// every peer renders the rope and the reel resolves identically on input + state authority.</summary>
		[Networked]
		public NetworkBool IsGrappling { get; private set; }
		/// <summary>World point the active grapple reels toward. Valid only while <see cref="IsGrappling"/>.</summary>
		[Networked]
		public Vector3 GrappleAnchor { get; private set; }
		[Networked]
		private TickTimer _grappleCooldown { get; set; }
		// Hard safety cap on a single reel, and a stall watchdog that detaches when the player stops closing on
		// the anchor (e.g. an obstacle is wedged between them and it). Networked so they survive prediction
		// rollback like the rest of the grapple state. _grappleBestDistance is the closest we've come so far.
		[Networked]
		private TickTimer _grappleTimeout { get; set; }
		[Networked]
		private TickTimer _grappleStallTimer { get; set; }
		[Networked]
		private float _grappleBestDistance { get; set; }
		// Reel speed of the current grapple, re-derived each tick from the held gadget on every predicting peer,
		// so it stays consistent without being networked itself.
		private float _grappleReelSpeed;

		// Detach if the player hasn't closed GrappleProgressEpsilon metres on the anchor within this many
		// seconds — the quick "I'm blocked" exit, well before the hard MaxReelTime cap.
		private const float GrappleStallSeconds   = 0.35f;
		private const float GrappleProgressEpsilon = 0.05f;

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

		/// <summary>True while the player is knocked out, getting up, mantling, seated in a vehicle, sleeping, or downed — input and active control are suppressed. Downed is a partial lock: PlayerInput re-enables crawl move + look on top of this.</summary>
		public bool IsInputLocked => RagdollState != ERagdollState.Normal || IsMantling || IsSeated || IsSleeping || IsDowned;

		private float _visualRollAngle;
		private float _deadSpinSpeedLocal;
		private float _bobPhase;
		private float _bobWeight;
		private float _baseFOV = -1f;
		// Standing capsule height and camera-pivot offset, captured on Spawned so SetHeight / camera
		// drop can be cleanly unwound when standing back up.
		private float _standHeight = -1f;
		private Vector3 _standCameraPivotLocalPos;
		// Last applied pivot→camera distance, smoothed across frames so the collision clamp eases in/out
		// instead of snapping. -1 = uninitialized (snap to target on the first frame).
		private float _smoothedCameraDist = -1f;

		// Animation IDs — first-person arms
		private int _animIDSpeedX;
		private int _animIDSpeedZ;
		private int _animIDMoveSpeedZ;
		private int _animIDGrounded;
		private int _animIDPitch;
		private int _animIDShoot;

		// Animation IDs — third-person body (shared param names with NpcAgent)
		private static readonly int _bodyAnimIDSpeed       = Animator.StringToHash("Speed");
		private static readonly int _bodyAnimIDMotionSpeed = Animator.StringToHash("MotionSpeed");
		private static readonly int _bodyAnimIDGrounded    = Animator.StringToHash("Grounded");
		private static readonly int _bodyAnimIDFreeFall    = Animator.StringToHash("FreeFall");
		private static readonly int _bodyAnimIDJump        = Animator.StringToHash("Jump");
		private static readonly int _bodyAnimIDIsClimbing   = Animator.StringToHash("IsClimbing");

		// Set by PlayerEmotes to suppress IK while an emote plays.
		private bool _ikSuppressedByEmote;

		private int _visibleFireCount;
		private int _visibleSecondaryFireCount;
		private Inventory _inventory;
		// AI brain for bot-controlled players; dormant (IsActive == false) on humans. Drives TryGetTickInput.
		private BotBrain _botBrain;
		// Skill tier for this bot, set in ConfigureAsBot (host only) and handed to the BotBrain on attach in Spawned.
		// Plain field — only the host runs bot AI, so it never needs to replicate. Ignored for humans.
		private BotDifficulty _botDifficulty = BotDifficulty.Medium;
		// Cached renderers under ScalingRoot — populated on Spawned for the local input authority only.
		// Toggled off while seated so the driver doesn't see their own torso/legs floating in the cab.
		private Renderer[] _localBodyRenderers;

		[Header("Spawn confinement (pre-round)")]
		[Tooltip("While waiting for all players to load in (before the round starts), the player is confined to a circle " +
			"of this radius around their spawn point and cannot interact. Once the round begins, movement is free.")]
		public float SpawnConfineRadius = 6f;

		/// <summary>Where this player spawned. Used to confine them to their spawn area during the pre-round wait.
		/// Written by the host once via <see cref="InitSpawnConfinement"/>; replicated so the input authority clamps
		/// to the same centre when predicting movement.</summary>
		[Networked] public Vector3 SpawnAnchor { get; private set; }

		/// <summary>True once <see cref="SpawnAnchor"/> has been set (host has spawned this player). Bots leave this
		/// false so they are never confined.</summary>
		[Networked] public NetworkBool SpawnAnchorSet { get; private set; }

		/// <summary>Host-only: record the spawn position so the player can be held in their spawn area until the round
		/// begins. Called by <see cref="GameManager.EnsurePlayerSpawned"/> right after the player is spawned.</summary>
		public void InitSpawnConfinement(Vector3 anchor)
		{
			if (HasStateAuthority == false) return;
			SpawnAnchor = anchor;
			SpawnAnchorSet = true;
		}


		
		// While the round hasn't begun (still waiting for everyone to load in), clamp the player's horizontal position
		// to a circle around their spawn anchor. Runs on both the state authority and the input authority's prediction,
		// off the same networked anchor + shared radius, so they agree. No-op once MatchManager.RoundStarted is true.
		private void ApplySpawnConfinement()
		{
			if (SpawnAnchorSet == false) return;
			if (MatchManager.RoundStarted) return;

			Vector3 pos = KCC.Position;
			Vector3 flat = pos - SpawnAnchor;
			flat.y = 0f;

			float maxSq = SpawnConfineRadius * SpawnConfineRadius;
			if (flat.sqrMagnitude <= maxSq) return;

			Vector3 clamped = SpawnAnchor + flat.normalized * SpawnConfineRadius;
			clamped.y = pos.y;
			KCC.SetPosition(clamped);
		}

		public void Respawn(Vector3 position, float yaw = 0f)
		{
			Health.Revive();

			KCC.SetActive(true);
			KCC.SetPosition(position);
			KCC.SetLookRotation(0f, yaw);

			// Replicate the spawn facing to the input authority so its accumulated look input is re-seeded
			// (KCC.SetLookRotation above only sticks until that player's next input tick overrides it).
			SpawnLookYaw = yaw;
			SpawnLookVersion++;

			_moveVelocity = Vector3.zero;
			Stamina = MaxStamina;
			_wasSprinting = false;
			_airSprintCarry = false;
			_staminaRegenTimer = default;
			_fallPeakDownSpeed = 0f;
			_wasGroundedForFall = true;

			IsDowned = false;
			DownedTimeRemaining = 0f;
			ReviveCount = 0;
			ReviveProgress = 0f;

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
			_climbGraceTimer = default;
			_climbWrapCommitTimer = default;
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

		/// <summary>Grant <paramref name="amount"/> money. State-authority only — callers running on every predicting peer are fine; remote peers no-op. Used by money pickups, loot rewards, sell prompts, quest completion.</summary>
		public void AddMoney(int amount)
		{
			if (HasStateAuthority == false) return;
			if (amount <= 0) return;
			Money += amount;
		}

		/// <summary>Try to spend <paramref name="amount"/> money. Returns false (no-op) if the player can't afford it or the caller isn't on the state authority. Use for shop purchases / crafting fees / etc.</summary>
		public bool TrySpendMoney(int amount)
		{
			if (HasStateAuthority == false) return false;
			if (amount <= 0) return false;
			if (Money < amount) return false;
			Money -= amount;
			return true;
		}

		/// <summary>Debug-only: routes an <see cref="AddMoney"/> grant from the controlling client up to the state
		/// authority so debug-console tools (e.g. Quantum Console <c>add_gold</c>) replicate correctly off-host.
		/// Called on the local player (input authority); executes on the host.</summary>
		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
		public void RPC_DebugAddMoney(int amount)
		{
			AddMoney(amount);
		}

		/// <summary>Debug-only: routes an <see cref="AddScraps"/> grant from the controlling client up to the state
		/// authority so debug-console tools (e.g. Quantum Console <c>add_scraps</c>) replicate correctly off-host.
		/// Called on the local player (input authority); executes on the host.</summary>
		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
		public void RPC_DebugAddScraps(int amount)
		{
			AddScraps(amount);
		}

		/// <summary>Debug-only: sets both the player's max stamina (<see cref="MaxStamina"/>) and current
		/// <see cref="Stamina"/> for debug-console tools (Quantum Console <c>set_stamina</c>). A negative value is
		/// treated as "max it out" (9999). Called on the local player (input authority); broadcast to every peer so
		/// the non-networked <see cref="MaxStamina"/> field (and the stamina bar that reads it) refresh everywhere —
		/// the state authority additionally writes the networked <see cref="Stamina"/>, which syncs back on its own.</summary>
		[Rpc(RpcSources.InputAuthority, RpcTargets.All)]
		public void RPC_DebugSetStamina(int value)
		{
			float target = value < 0 ? 9999f : value;
			MaxStamina = target;
			_staminaRegenTimer = default;
			if (HasStateAuthority)
				Stamina = target;
		}

		/// <summary>Restore <paramref name="amount"/> stamina, clamped to <see cref="MaxStamina"/>. State-authority
		/// only — remote peers no-op (the networked <see cref="Stamina"/> syncs back on its own). Used by the
		/// energy-drink consumable (<c>StaminaCapability</c>).</summary>
		public void RestoreStamina(float amount)
		{
			if (HasStateAuthority == false) return;
			if (amount <= 0f) return;
			Stamina = Mathf.Min(MaxStamina, Stamina + amount);
		}

		/// <summary>Grant <paramref name="amount"/> scraps. State-authority only — remote peers no-op. Used by the crafting bench when an item is scavenged.</summary>
		public void AddScraps(int amount)
		{
			if (HasStateAuthority == false) return;
			if (amount <= 0) return;
			Scraps += amount;
		}

		/// <summary>Try to spend <paramref name="amount"/> scraps. Returns false (no-op) if the player can't afford it or the caller isn't on the state authority. Use for recipe ScrapCost.</summary>
		public bool TrySpendScraps(int amount)
		{
			if (HasStateAuthority == false) return false;
			if (amount <= 0) return true; // nothing to spend is a trivial success
			if (Scraps < amount) return false;
			Scraps -= amount;
			return true;
		}

		/// <summary>Client-safe roster of every spawned Player, maintained on ALL peers (authority and remotes).
		/// Unlike <see cref="GameManager.Players"/> — which is only valid on the state authority — this is the roster
		/// local presentation code (team panel, ally/enemy world markers) iterates. Registered in <see cref="Spawned"/>,
		/// removed in <see cref="Despawned"/>.</summary>
		public static readonly System.Collections.Generic.List<Player> All = new(32);

		public override void Spawned()
		{
			if (All.Contains(this) == false)
				All.Add(this);

			// Player draws its own death visual via the ragdoll tilt — keep the body
			// visible past death instead of letting Health.Render hide VisualRoot.
			Health.SuppressDeathVisualSwap = true;

			// Intercept lethal damage so the player enters the downed state instead of dying
			// outright. The hook only fires on state authority (Health checks HasStateAuthority
			// before invoking it), so wiring it on every peer is harmless. Bots skip the downed
			// flow — they have no allies to revive them, so they die outright and the last-team-
			// standing scan resolves on the killing blow instead of after a bleed-out.
			if (IsBot == false)
				Health.AuthorityDownHook = OnLethalDamage;

			// Attach a programmatic InteractionPrompt so allies see a "revive me" indicator
			// while the player is downed. Opt into HideWhenLocked so the prompt only appears
			// while the IInteractable side reports CanInteract == true (i.e. IsDowned).
			SetupDownedInteractionPrompt();

			// Re-seed SimpleKCC's internal pose from the spawn position. Without this, the KCC's
			// first-tick depenetration can run from the prefab's serialized transform (often 0,0,0)
			// instead of the spawn point and tunnel the player through the floor.
			KCC.SetActive(true);
			KCC.SetPosition(transform.position);
			// Face the spawn point's yaw (replicated via the spawn rotation) instead of always world +Z,
			// so the player looks the way the SpawnPoint arrow gizmo points.
			KCC.SetLookRotation(0f, transform.rotation.eulerAngles.y);

			// Cap the KCC's walkable ground angle at the climb threshold so "steep" actually means
			// non-walkable: surfaces steeper than ClimbForceAngle are treated as walls, not ground.
			// The player then meets a steep face head-on (instead of strolling up it), and the grounded
			// force-climb in ProcessInput can latch it — provided it's on the Climbable layer. Set on
			// every peer so movement prediction stays consistent across clients.
			KCC.SetMaxGroundAngle(ClimbForceAngle);

			_standHeight = KCC.Settings.Height;
			if (CameraPivot != null) _standCameraPivotLocalPos = CameraPivot.localPosition;

			// Disable root motion and NavMeshAgent on both body animators — KCC owns movement.
			foreach (var anim in new[] { BodyAnimator, NoHeadAnimator })
			{
				if (anim == null) continue;
				anim.applyRootMotion = false;
				var navAgent = anim.GetComponent<UnityEngine.AI.NavMeshAgent>();
				if (navAgent != null) navAgent.enabled = false;
			}

			// NoHeadAnimator is the local player's view — the body is mostly behind the first-person
			// camera so CullUpdateTransforms would freeze its bones. Force AlwaysAnimate on every
			// animator in the hierarchy (parent + nested Character_Daughter_01) so IK and animation
			// keep running even when the mesh is off-screen.
			if (NoHeadAnimator != null)
				foreach (var anim in NoHeadAnimator.GetComponentsInChildren<Animator>(true))
					anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;



			if (HasStateAuthority)
			{
				// Bots get their synthetic Owner in ConfigureAsBot (onBeforeSpawned); humans bind theirs here.
				if (IsBot == false)
					Owner = Object.InputAuthority;

				Stamina = MaxStamina;
				Money = StartingMoney;
				Scraps = StartingScraps;
			}

			if (IsBot)
			{
				// Give bots a readable name so nameplates / team previews don't show a blank.
				if (HasStateAuthority && string.IsNullOrEmpty(Nickname))
					Nickname = $"Bot {Owner.PlayerId - GameManager.BotRefBase + 1}";

				// Attach the AI brain at runtime — ONLY for an authoritative bot. The shared Player prefab carries
				// no BotBrain (a human player is not an AI), so humans and proxies never get one. BotBrain is a plain
				// MonoBehaviour with no networked state (like the planning NavMeshAgent it spins up), so adding it here
				// is safe and never touches the Fusion bake. KCC is already active + positioned above, so _home is correct.
				if (HasStateAuthority)
				{
					_botBrain = gameObject.AddComponent<BotBrain>();
					_botBrain.Difficulty = _botDifficulty;
					_botBrain.Initialize();
				}
			}
			else if (HasInputAuthority)
			{
				// Sending player nickname that is saved in UIGameMenu
				RPC_SetNickname(PlayerPrefs.GetString("PlayerName"));
			}

			// In case the nickname is already changed,
			// we need to trigger the change manually
			OnNicknameChanged();

			// Reset visible fire counts
			_visibleFireCount = _fireCount;
			_visibleSecondaryFireCount = _secondaryFireCount;

			if (HasInputAuthority)
			{
				// For input authority deactivate head renderers so they are not obstructing the view
				for (int i = 0; i < HeadRenderers.Length; i++)
				{
					HeadRenderers[i].shadowCastingMode = ShadowCastingMode.ShadowsOnly;
				}

				// Cache local body renderers so we can hide them while seated (the camera is at
				// driver-eye-level inside the cab and the torso/legs would otherwise block the view).
				if (ScalingRoot != null)
				{
					_localBodyRenderers = ScalingRoot.GetComponentsInChildren<Renderer>(true);
				}

				// Held weapon is moved to FirstPersonOverlay layer by Inventory.RefreshHeldItem
				// to prevent clipping when close to a wall.

				// Look rotation interpolation is skipped for local player.
				// Look rotation is set manually in Render.
				KCC.Settings.ForcePredictedLookRotation = true;
			}
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			All.Remove(this);
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

			// Downed authority must run BEFORE CheckDeathRagdoll so a bleed-out kill that fires
			// this tick can flip RagdollState = Dead immediately, without a one-tick freeze where
			// the body sits in the downed-crouch pose before tumbling.
			if (IsDowned)
			{
				UpdateDownedAuthority();
			}

			CheckDeathRagdoll();

			if (IsDowned)
			{
				ProcessDownedTick();
				HitboxRoot.HitboxRootActive = Health.IsAlive;
				KCC.SetActive(true);
				return;
			}

			bool drainedThisTick = false;
			if (IsMantling)
			{
				// Mantle takes priority over normal input handling — the player is in a forced
				// animation finishing the climb-up onto the ledge. Input is locked via IsInputLocked.
				ProcessMantle();
			}
			else if (Health.IsAlive && RagdollState == ERagdollState.Normal && IsSleeping == false && IsEmoting == false && TryGetTickInput(out var input, out var previousButtons))
			{
				drainedThisTick = IsClimbing
					? ProcessClimbInput(input, previousButtons)
					: ProcessInput(input, previousButtons);
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
					if (DebugClimbProbes) Debug.Log($"[Climb] EXIT via no-input/ragdoll path: alive={Health.IsAlive} ragdoll={RagdollState} sleeping={IsSleeping}");
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
				Stamina = Mathf.Min(MaxStamina, Stamina + StaminaRegenPerSecond * Runner.DeltaTime);
			}

			if (KCC.IsGrounded)
			{
				// Stop jumping
				_isJumping = false;
			}

			// Landing damage — runs after movement so KCC.RealVelocity reflects this tick's fall speed.
			ProcessFallDamage();

			// Pre-round lockdown: keep the player inside their spawn area until the round begins (everyone loaded in).
			ApplySpawnConfinement();

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
				if (!ShowFullBodyForEmote)
					KCC.SetLookRotation(Input.LookRotation, -90f, 90f);
				// Camera orbit during ShowFullBody emotes is handled in LateUpdate.
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

			if (Animator != null && Animator.runtimeAnimatorController != null)
			{
				Animator.SetFloat(_animIDSpeedX, moveSpeed.x, 0.1f, Time.deltaTime);
				Animator.SetFloat(_animIDSpeedZ, moveSpeed.z, 0.1f, Time.deltaTime);
				Animator.SetBool(_animIDGrounded, KCC.IsGrounded);
				Animator.SetFloat(_animIDPitch, KCC.GetLookRotation(true, false).x, 0.02f, Time.deltaTime);
			}

			{
				float horizontalSpeed = new Vector2(KCC.RealVelocity.x, KCC.RealVelocity.z).magnitude;
				DriveBodyAnimator(BodyAnimator,   horizontalSpeed);
				DriveBodyAnimator(NoHeadAnimator, horizontalSpeed);
			}

			// Per-hand IK toggle — live so each can be flipped independently during play.

			// IK is suppressed while climbing, during an emote, or when no item is held.
			bool hasHeldItem = _inventory?.HeldInstance != null;
			bool ikSuppressed = IsClimbing || _ikSuppressedByEmote || !hasHeldItem;
			bool rightIK = UseRightArmIK && !ikSuppressed;
			bool leftIK  = UseLeftArmIK  && !ikSuppressed;

			// Track RightHandTarget to HandAnchor so the arm reaches the held item position.
			if (rightIK && RightHandTarget != null && _inventory?.HandAnchor != null)
			{
				RightHandTarget.position = _inventory.HandAnchor.position;
				RightHandTarget.rotation = _inventory.HandAnchor.rotation;
			}

			foreach (var anim in new[] { BodyAnimator, NoHeadAnimator })
			{
				if (anim == null) continue;

				// Skip NoHeadAnimator if IK should only apply to PlayerFullBody.
				bool isNoHead = anim == NoHeadAnimator;
				bool applyIK  = !isNoHead || !UseIKOnlyOnFullBody;

				var rb = anim.GetComponent<UnityEngine.Animations.Rigging.RigBuilder>();
				bool needsIK = applyIK && (rightIK || leftIK);
				if (rb != null && rb.enabled != needsIK) rb.enabled = needsIK;

				var constraints = anim.GetComponentsInChildren<UnityEngine.Animations.Rigging.TwoBoneIKConstraint>(true);
				foreach (var c in constraints)
				{
					if (c.gameObject.name == "Right_Arm_IK")
						c.weight = (applyIK && rightIK) ? 1f : 0f;
					else if (c.gameObject.name == "Left_Arm_IK")
						c.weight = (applyIK && leftIK)  ? 1f : 0f;
				}
			}

			// Body visibility — updated every frame so the debug toggle responds live.
			// Local player sees NoHead; others see FullBody. Debug flag shows both.
			if (BodyAnimator != null)
			{
				bool hideFullBody = HasInputAuthority && !ShowBothBodiesDebug && !ShowFullBodyForEmote;
				var mode = hideFullBody ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
				foreach (var r in BodyAnimator.GetComponentsInChildren<Renderer>(true))
					if (r.shadowCastingMode != mode) r.shadowCastingMode = mode;
			}
			if (NoHeadAnimator != null)
			{
				bool hideNoHead = HasInputAuthority ? ShowFullBodyForEmote && !ShowBothBodiesDebug
				                                   : !ShowBothBodiesDebug;
				var mode = hideNoHead ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
				foreach (var r in NoHeadAnimator.GetComponentsInChildren<Renderer>(true))
					if (r.shadowCastingMode != mode) r.shadowCastingMode = mode;
			}

			FootstepSound.enabled = KCC.IsGrounded && KCC.RealSpeed > 1f;
			ScalingRoot.localScale = Vector3.Lerp(ScalingRoot.localScale, Vector3.one, Time.deltaTime * 8f);

			var emission = DustParticles.emission;
			emission.enabled = KCC.IsGrounded && KCC.RealSpeed > 1f;

			ShowFireEffects();
			UpdateMeleeChargingVisual();
		}

		private void UpdateMeleeChargingVisual()
		{
			var visual = GetHeldVisual();
			if (visual == null) return;

			var action = GetActiveAction();
			if (action == null || action.Charge.Enabled == false)
			{
				visual.SetCharging(false, 0f);
				return;
			}

			visual.SetCharging(ActionInvoker.IsCharging, ActionInvoker.ChargeProgress(action));
		}


		private void Awake()
		{
			AssignAnimationIDs();
			_inventory = GetComponent<Inventory>();
			if (ActionInvoker == null) ActionInvoker = GetComponent<ActionInvoker>();
			// _botBrain is intentionally NOT fetched here — the shared prefab has no BotBrain. It is added at
			// runtime in Spawned() only for authoritative bots (see the IsBot block), so humans never carry one.
		}

		/// <summary>Marks this Player as an AI bot and stamps its synthetic <see cref="Owner"/> ref. Called by
		/// <see cref="GameManager.AddBots"/> inside <c>Runner.Spawn</c>'s <c>onBeforeSpawned</c> on the host, so the
		/// [Networked] state is valid before <see cref="Spawned"/> runs. No-op off the state authority.</summary>
		public void ConfigureAsBot(PlayerRef syntheticOwner, string botName = null, BotDifficulty difficulty = BotDifficulty.Medium)
		{
			if (HasStateAuthority == false) return;
			IsBot = true;
			Owner = syntheticOwner;
			_botDifficulty = difficulty;
			if (string.IsNullOrEmpty(botName) == false)
				Nickname = botName;
		}

		/// <summary>Host-only: re-tune this bot's difficulty at runtime (e.g. the <c>bot_difficulty</c> command). Updates
		/// the live <see cref="BotBrain"/> if attached and remembers the tier. No-op on humans / off the state authority.</summary>
		public void SetBotDifficulty(BotDifficulty difficulty)
		{
			if (HasStateAuthority == false || IsBot == false) return;
			_botDifficulty = difficulty;
			if (_botBrain != null) _botBrain.SetDifficulty(difficulty);
		}

		/// <summary>Single input seam for <see cref="FixedUpdateNetwork"/>. Bots pull a freshly-computed
		/// <see cref="GameplayInput"/> from their <see cref="BotBrain"/>; humans read the networked input buffer.
		/// Everything downstream (ProcessInput / ProcessClimbInput) is identical for both.</summary>
		private bool TryGetTickInput(out GameplayInput input, out NetworkButtons previousButtons)
		{
			if (_botBrain != null && _botBrain.IsActive)
				return _botBrain.ProduceInput(out input, out previousButtons);

			previousButtons = default;
			if (GetInput<GameplayInput>(out input))
			{
				previousButtons = Input.PreviousButtons;
				return true;
			}
			return false;
		}

		private void LateUpdate()
		{
			// Keep the remote player's nameplate relation indicator current (team/phase can change at any time).
			// Done before the early returns below so seated / downed players still show the right colour.
			UpdateNameplateRelation();

			if (IsSeated)
			{
				SeatedLateUpdate();
				return;
			}

			// Lock the character visual to face the wall while climbing, then restore normal
			// inherited rotation when climbing ends so the body tracks the KCC again.
			// Done in LateUpdate so it runs after Fusion's Render() and KCC transform updates.
			ApplyClimbRotation(BodyAnimator);
			ApplyClimbRotation(NoHeadAnimator);

			bool isDeadRagdoll = RagdollState == ERagdollState.Dead;

			if (Health.IsAlive == false && isDeadRagdoll == false)
				return;

			// Update camera pivot (influences ChestIK)
			// (KCC look rotation is set earlier in Render)
			var pitchRotation = KCC.GetLookRotation(true, false);

			Quaternion ragdollTilt = ComputeRagdollTumbleRotation();
			CameraPivot.localRotation = ragdollTilt * Quaternion.Euler(pitchRotation);
			ScalingRoot.localRotation = ragdollTilt;

			// Local-only: ease the camera pivot toward its crouched / standing / downed local
			// position. Driven from the networked IsDowned / IsCrouching flags so the lerp starts
			// the moment the state replicates. Standing pose is captured on Spawned.
			if (HasInputAuthority && _standHeight > 0f)
			{
				float drop = IsDowned ? DownedCameraDrop : (IsCrouching ? CrouchCameraDrop : 0f);
				Vector3 target = _standCameraPivotLocalPos + new Vector3(0f, drop, 0f);
				CameraPivot.localPosition = Vector3.Lerp(CameraPivot.localPosition, target, CrouchCameraLerpSpeed * Time.deltaTime);
			}

			if (isDeadRagdoll == false && ChestBone != null && ChestTargetPosition != null)
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
				if (ShowFullBodyForEmote && Input != null && EmoteCameraOffset != Vector3.zero)
				{
					// Orbit mode: camera driven entirely from mouse look, bypassing CameraPivot/KCC.
					Quaternion orbitRot = Quaternion.Euler(Input.LookRotation.x, Input.LookRotation.y, 0f);
					Vector3 orbitPos    = transform.position + Vector3.up * 1.5f + orbitRot * EmoteCameraOffset;
					Camera.main.transform.SetPositionAndRotation(orbitPos, orbitRot);
				}
				else
				{
					Camera.main.transform.SetPositionAndRotation(CameraHandle.position, CameraHandle.rotation);
				}

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

			// Resolve the clamp target. Default to the full desired distance (no blocker).
			float targetDist = desiredDist;
			if (Physics.SphereCast(pivotPos, radius, dir, out RaycastHit hit, desiredDist, mask, QueryTriggerInteraction.Ignore))
			{
				// hit.distance == 0 means the sphere was ALREADY overlapping a collider at the pivot
				// (e.g. standing close to a wall in front — the wall is within `radius` of the head).
				// That's not a blocker sitting between the pivot and the eye, so clamping here would
				// snap the camera all the way down onto the pivot. Treat it as "no clamp" (full distance);
				// only clamp for genuine blockers the eye-offset sweeps INTO (the climb / look-up case).
				if (hit.distance > 0.0001f)
				{
					targetDist = Mathf.Max(0f, hit.distance - CameraCollisionPadding);
				}
			}

			// Ease the applied distance toward the target instead of snapping. Without this, the SphereCast
			// flips abruptly between "clamped in" (eye-offset sweep clips the wall) and "full extension"
			// (sphere initially overlaps → ignored above) as you close the last few cm to a wall — that flip
			// is the visible jump. Smoothing turns it into a soft push/pull. -1 = first frame, snap.
			if (_smoothedCameraDist < 0f) _smoothedCameraDist = targetDist;
			else _smoothedCameraDist = Mathf.Lerp(_smoothedCameraDist, targetDist, 1f - Mathf.Exp(-CameraCollisionLerpSpeed * Time.deltaTime));

			cam.transform.position = pivotPos + dir * _smoothedCameraDist;
		}

		private void ApplySprintFOV()
		{
			var cam = Camera.main;
			if (cam == null) return;

			if (_baseFOV < 0f) _baseFOV = cam.fieldOfView;

			// Aiming (ADS) zooms in and suppresses the sprint boost, per the held weapon's AimTuning.
			var aim = IsAiming && _inventory != null ? _inventory.ActiveWeapon?.Aim : null;

			float horizontalSpeed = new Vector2(KCC.RealVelocity.x, KCC.RealVelocity.z).magnitude;
			float sprintT = Mathf.InverseLerp(WalkSpeed, SprintSpeed, horizontalSpeed);
			bool canBoost = Health.IsAlive && RagdollState == ERagdollState.Normal && aim == null;

			float targetFOV = _baseFOV + (canBoost ? SprintFOVBoost * sprintT : 0f) - (aim != null ? aim.FOVReduction : 0f);
			float lerpSpeed = aim != null ? aim.FOVLerpSpeed : FOVLerpSpeed;

			cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, lerpSpeed * Time.deltaTime);
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
			// While scoped (ADS), Sprint is repurposed as the breath-hold (steady aim) — so it never sprints.
			float startThreshold = _wasSprinting ? 0f : MinStaminaToStartSprint;
			bool isSprinting = !IsCrouching && !knockbackActive && wantsSprint && isMoving && IsAiming == false && Stamina > startThreshold;

			// Sprint-jump momentum carry: latched at takeoff (see jump block below), cleared on landing.
			// While airborne and latched, treat the player as sprinting for speed purposes even if they
			// release Sprint mid-air. Stamina was already paid on the ground, so no extra drain here.
			if (KCC.IsGrounded) _airSprintCarry = false;
			bool airSprinting = _airSprintCarry && !KCC.IsGrounded && isMoving && !IsCrouching && !knockbackActive;

			float speed;
			if (IsCrouching && KCC.IsGrounded) speed = CrouchSpeed;
			else if (isSprinting || airSprinting) speed = SprintSpeed;
			else speed = WalkSpeed;
			// Holding Sprint while scoped is the breath-hold (steady aim) — creep at half speed.
			if (IsAiming && wantsSprint) speed *= 0.5f;
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

			// Sniper breath-hold: holding Sprint while scoped steadies the sway (applied locally in
			// PlayerInput) and burns stamina fast here in the sim. Regen is pinned off for as long as
			// the button is held, so once it empties it stays empty until the player releases — the
			// view-side depletion recoil then fires exactly once.
			if (IsAiming && wantsSprint)
			{
				_staminaRegenTimer = TickTimer.CreateFromSeconds(Runner, StaminaRegenDelay);
				if (Stamina > 0f)
				{
					Stamina = Mathf.Max(0f, Stamina - BreathHoldStaminaDrainPerSecond * Runner.DeltaTime);
					drained = true;
				}
			}

			float jumpImpulse = 0f;

			bool jumpPressed = !knockbackActive && !IsCrouching && input.Buttons.WasPressed(previousButtons, EInputButton.Jump);

			// Mid-air grab attempt: pressing Jump in the air OR holding forward into a wall while airborne
			// tries to latch onto a climbable surface. The grounded jump-press path below never attempts
			// to grab, so a vertical jump is always preserved when standing next to a wall.
			bool wantsMidAirGrab = !KCC.IsGrounded && (jumpPressed || input.MoveDirection.y > 0.5f);
			if (wantsMidAirGrab && TryEnterClimb(MaxClimbSurfaceAngle))
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

			// Grounded force-climb: pushing forward into a climbable surface that's too steep to walk
			// (steeper than ClimbForceAngle) auto-engages a climb instead of stalling against it. The
			// Stamina > 0 gate stops an instant re-grab right after a depleted slide drops you to the
			// ground; gentle ramps fall below ClimbForceAngle and stay walkable by the KCC.
			bool wantsGroundForceClimb = KCC.IsGrounded
				&& CanClimb
				&& Stamina > 0f
				&& !IsCrouching
				&& input.MoveDirection.y > 0.5f;
			if (wantsGroundForceClimb && TryEnterClimb(ClimbForceAngle))
			{
				if (ActionInvoker != null && ActionInvoker.IsCharging)
				{
					ActionInvoker.CancelCharge();
				}
				_wasSprinting = false;
				return drained;
			}

			// Comparing current input buttons to previous input buttons - this prevents glitches when input is lost
			if (jumpPressed && KCC.IsGrounded)
			{
				// Set world space jump vector
				jumpImpulse = JumpImpulse;
				_isJumping = true;
				_airSprintCarry = isSprinting;

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

			// Steep-slope walk block: SetMaxGroundAngle stops a steep face from counting as ground, but the
			// KCC's collision resolution can still let forward input creep the player up it. Strip the
			// into-surface component from the desired velocity so a too-steep face can't be walked up — the
			// player slides back down under gravity instead. Climbable steep faces never reach here (the
			// grounded force-climb above already grabbed and returned); this catches the non-climbable ones.
			if (!knockbackActive)
			{
				desiredMoveVelocity = BlockSteepSlopeMovement(desiredMoveVelocity);
			}

			// Refresh / detach the grapple before moving so the reel velocity applied below is current.
			UpdateGrappleTick();

			MovePlayer(desiredMoveVelocity, jumpImpulse);

			// Update camera pivot so fire transform (CameraHandle) is correct
			var pitchRotation = KCC.GetLookRotation(true, false);
			CameraPivot.localRotation = Quaternion.Euler(pitchRotation);

			ProcessFireInput(input, previousButtons);
			ProcessSecondaryInput(input, previousButtons);

			// Tick the magazine auto-reload cycle (no-op for non-magazine weapons). Runs on SA + IA;
			// mutations gate on state authority internally, the reload timer/flag replicate for the HUD.
			_inventory?.UpdateAmmoCycle();

			return drained;
		}

		private void ProcessFireInput(GameplayInput input, NetworkButtons previousButtons)
		{
			// Carrying a placeable: LMB is handled locally by PlacementController (which sends
			// the place RPC directly). The selected hotbar slot is inert while carrying — your
			// hands are full — so suppress weapon-fire / consume logic and cancel any stale charge.
			if (_inventory != null && _inventory.CarriedPlaceableId != 0)
			{
				if (ActionInvoker != null && ActionInvoker.IsCharging) ActionInvoker.CancelCharge();
				return;
			}

			// Consumable in hand: LMB press consumes one and applies its effect.
			// Cancel any stale charge so re-equipping a melee weapon can't release a charged swing.
			if (_inventory != null && _inventory.SelectedDefinition != null
			    && _inventory.SelectedDefinition.HasCapability<ConsumableCapability>())
			{
				if (ActionInvoker != null && ActionInvoker.IsCharging) ActionInvoker.CancelCharge();
				if (input.Buttons.WasPressed(previousButtons, EInputButton.Fire))
				{
					_inventory.TryUseSelectedConsumable();
				}
				return;
			}

			// Gadget in hand that claims LMB: route the press to the gadget instead of the weapon fire path.
			// The grapple is networked (it reels the KCC) so it is handled here on the player; a purely-local
			// gadget gets its OnUsePressed hook fired on the owning client. Passive gadgets (radar, UsesPrimary
			// false) fall through to the no-op weapon path below. An item carrying both a weapon and a primary
			// gadget can't sensibly share LMB; the gadget wins.
			if (_inventory != null && _inventory.SelectedDefinition != null
			    && _inventory.SelectedDefinition.TryGetCapability<GadgetCapability>(out var gadget)
			    && gadget.UsesPrimary)
			{
				if (ActionInvoker != null && ActionInvoker.IsCharging) ActionInvoker.CancelCharge();

				if (gadget is GrappleGadgetCapability grappleCfg)
					ProcessGrappleInput(grappleCfg, input, previousButtons);
				else if (HasInputAuthority && input.Buttons.WasPressed(previousButtons, EInputButton.Fire))
					_inventory.UseSelectedGadgetLocal();
				return;
			}

			var action = GetActiveAction();
			if (action == null)
			{
				// Nothing to fire with — drop any stale charge so it doesn't bleed into the next held item.
				if (ActionInvoker != null && ActionInvoker.IsCharging) ActionInvoker.CancelCharge();
				return;
			}

			// PvP arming gate (weapons only — placeables/consumables above still work in town): no weapon fires
			// outside Night, and during Night only after the player has reached their team zone (see ZoneManager).
			// Exception: actions flagged AllowInPeacePhase (the unarmed punch) fire in any phase — the shove still
			// lands, but Health.TakeHit's PvP gate keeps it damage-free during the day. Drop any in-progress charge
			// so a real weapon can't release once disarmed.
			if (action.AllowInPeacePhase == false && CanFireWeaponNow() == false)
			{
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
					float norm = action.Charge.ThresholdSeconds > 0f
						? Mathf.Clamp01(chargeSeconds / action.Charge.ThresholdSeconds)
						: 1f;
					Fire(action, charged, norm);
				}
			}
		}

		// Grapple gadget LMB handling. Runs on input + state authority like ProcessFireInput; the [Networked]
		// writes (IsGrappling / GrappleAnchor / cooldown / charge spend) are predicted on the input authority
		// and reconciled to proxies. A second press while reeling detaches.
		private void ProcessGrappleInput(GrappleGadgetCapability cfg, GameplayInput input, NetworkButtons previousButtons)
		{
			if (input.Buttons.WasPressed(previousButtons, EInputButton.Fire) == false) return;

			if (IsGrappling) { IsGrappling = false; return; }   // toggle off

			if (_grappleCooldown.ExpiredOrNotRunning(Runner) == false) return;
			if (RagdollState != ERagdollState.Normal || IsSleeping || IsClimbing) return;
			if (_inventory == null || _inventory.ActiveCharges <= 0) return;

			// Deterministic PhysX ray against the static world (no networked hitboxes involved), so the anchor
			// resolves identically on IA and SA — the predicted reel matches the authoritative one without an RPC.
			var physics = Runner.GetPhysicsScene();
			if (physics.Raycast(CameraHandle.position, CameraHandle.forward, out var hit, cfg.Range, CombatAction.HitMask))
			{
				GrappleAnchor = hit.point;
				IsGrappling = true;
				_grappleReelSpeed = cfg.ReelSpeed;
				_grappleCooldown = TickTimer.CreateFromSeconds(Runner, cfg.FireCooldown);
				// Arm the exit watchdogs: a hard cap and a stall window seeded from the starting distance.
				float maxReel = cfg.MaxReelTime > 0f ? cfg.MaxReelTime : 3f;
				_grappleTimeout = TickTimer.CreateFromSeconds(Runner, maxReel);
				_grappleStallTimer = TickTimer.CreateFromSeconds(Runner, GrappleStallSeconds);
				_grappleBestDistance = Vector3.Distance(transform.position, GrappleAnchor);
				_inventory.TryConsumeCharge();   // predicted via the networked Loaded write; spent forever
			}
		}

		// Per-tick grapple maintenance: refresh the reel speed from the held gadget and detach on arrival or
		// when the grapple is no longer usable (dropped / switched slots / knocked down / climbing). Called
		// before MovePlayer so ComputeGrappleVelocity sees the up-to-date state.
		private void UpdateGrappleTick()
		{
			if (IsGrappling == false) return;

			var cfg = _inventory != null && _inventory.SelectedDefinition != null
				? _inventory.SelectedDefinition.GetCapability<GrappleGadgetCapability>()
				: null;

			if (cfg == null || RagdollState != ERagdollState.Normal || IsSleeping || IsClimbing)
			{
				IsGrappling = false;
				return;
			}

			_grappleReelSpeed = cfg.ReelSpeed;

			float dist = Vector3.Distance(transform.position, GrappleAnchor);

			// Arrived — clean stop.
			if (dist <= cfg.ArrivalDistance) { IsGrappling = false; return; }

			// Hard safety cap — never reel forever, whatever the cause.
			if (_grappleTimeout.Expired(Runner)) { IsGrappling = false; return; }

			// Stall watchdog: still closing in → keep going and refresh the window; otherwise, if we've made no
			// real progress for GrappleStallSeconds (an obstacle wedged between us and the anchor), let go so the
			// player can't get stuck grinding against it.
			if (dist < _grappleBestDistance - GrappleProgressEpsilon)
			{
				_grappleBestDistance = dist;
				_grappleStallTimer = TickTimer.CreateFromSeconds(Runner, GrappleStallSeconds);
			}
			else if (_grappleStallTimer.Expired(Runner))
			{
				IsGrappling = false;
			}
		}

		// Reel velocity folded into KCC.Move (see MovePlayer), alongside knockback. Straight pull toward the
		// anchor at the gadget's reel speed; zero unless actively grappling.
		private Vector3 ComputeGrappleVelocity()
		{
			if (IsGrappling == false) return Vector3.zero;
			Vector3 toAnchor = GrappleAnchor - transform.position;
			float d = toAnchor.magnitude;
			return d > 0.001f ? toAnchor / d * _grappleReelSpeed : Vector3.zero;
		}

		/// <summary>True if the local player is holding a grapple gadget (any charge state). Drives the HUD
		/// reticle's visibility; pair with <see cref="IsGrappleTargetValid"/> for the in-range state.</summary>
		public bool IsHoldingGrapple => _inventory != null && _inventory.SelectedDefinition != null
			&& _inventory.SelectedDefinition.HasCapability<GrappleGadgetCapability>();

		/// <summary>
		/// Local HUD check for the grapple reticle: true when a grapple gadget with charges left is held and the
		/// player is aiming at a surface within reel range. Cosmetic only — runs off the network tick on the
		/// local client, using the same PhysX ray + range as the actual fire so the indicator matches what a
		/// press would actually hit.
		/// </summary>
		public bool IsGrappleTargetValid()
		{
			if (_inventory == null || Runner == null || CameraHandle == null) return false;
			var def = _inventory.SelectedDefinition;
			var cfg = def != null ? def.GetCapability<GrappleGadgetCapability>() : null;
			if (cfg == null || _inventory.ActiveCharges <= 0) return false;

			return Runner.GetPhysicsScene()
				.Raycast(CameraHandle.position, CameraHandle.forward, out _, cfg.Range, CombatAction.HitMask);
		}

		// Secondary (RMB) handling, driven by the held weapon's SecondaryMode:
		//   Attack — fire SecondaryAction on press (e.g. throw), on its own cooldown.
		//   Aim    — hold to enter the ADS stance (IsAiming): FOV zoom + steadier sway/recoil + tighter spread.
		// Runs on input + state authority like ProcessFireInput; the [Networked] IsAiming write is predicted on IA.
		private void ProcessSecondaryInput(GameplayInput input, NetworkButtons previousButtons)
		{
			// Hands full with a placeable, or a consumable selected: no secondary action — and never
			// leave the player stuck in the aim stance.
			bool suppressed = _inventory == null
				|| _inventory.CarriedPlaceableId != 0
				|| (_inventory.SelectedDefinition != null && _inventory.SelectedDefinition.HasCapability<ConsumableCapability>());

			var weapon = suppressed ? null : _inventory.ActiveWeapon;
			var mode = weapon != null ? weapon.SecondaryMode : ESecondaryMode.None;

			// Recompute the aim stance every tick so switching to a non-aim weapon, climbing, or any
			// input lock drops it immediately.
			bool wantAim = mode == ESecondaryMode.Aim
				&& input.Buttons.IsSet(EInputButton.SecondaryFire)
				&& IsInputLocked == false
				&& IsClimbing == false;
			if (IsAiming != wantAim) IsAiming = wantAim;

			if (mode == ESecondaryMode.Attack && weapon.SecondaryAction != null && CanFireWeaponNow())
			{
				if (input.Buttons.WasPressed(previousButtons, EInputButton.SecondaryFire))
					FireSecondary(weapon.SecondaryAction);
			}
		}

		// Mirror of Fire for the secondary action — runs it through the invoker's independent
		// SecondaryCooldown and drives the throw-swing feedback via _secondaryFireCount.
		private void FireSecondary(CombatAction action)
		{
			if (action == null || ActionInvoker == null) return;
			if (CanFireWeaponNow() == false) return; // authoritative safety net behind the input gate

			_hitPosition = Vector3.zero;
			_hitNormal = Vector3.zero;

			var ctx = new ActorContext
			{
				Runner = Runner,
				IgnoreAuthority = Owner,
				AttackerPosition = transform.position,
				FireTransform = CameraHandle,
				AttackerRoot = gameObject,
				IsStateAuthority = HasStateAuthority,
				ChargeNormalized = 0f,
				IsAiming = IsAiming,
			};

			var hit = ActionInvoker.TryFire(action, in ctx, false, secondary: true);
			if (hit.DidFire == false) return;

			if (hit.DidHit)
			{
				_hitPosition = hit.Point;
				_hitNormal = hit.Normal;
			}

			_secondaryFireCount++;
		}

		// BOTW-style climb anchor: cast forward (yaw-only, ignoring pitch so looking up at the wall doesn't reject the grab)
		// from the chest. If a steep enough surface is hit on the ClimbableMask, snap the KCC to the anchor distance and
		// flip IsClimbing on. Caller (ProcessInput) decides whether to suppress the normal jump impulse.
		// State-authority-only via the mutation of [Networked] fields below — proxies see the change replicate.
		private bool TryEnterClimb(float minSurfaceAngle)
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

			// Reject too-horizontal surfaces (floors, gentle ramps). Threshold is supplied by the
			// caller: the deliberate mid-air grab demands a near-vertical wall (MaxClimbSurfaceAngle),
			// while the grounded force-climb engages on anything too steep to walk (ClimbForceAngle).
			if (Vector3.Angle(hit.normal, Vector3.up) < minSurfaceAngle) return false;

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
			// Pitch is free; yaw is clamped to ±90° from wall-facing so the player can't spin away.
			float wallFacingYaw = Mathf.Atan2(-_climbWallNormal.x, -_climbWallNormal.z) * Mathf.Rad2Deg;
			float clampedYaw    = wallFacingYaw + Mathf.Clamp(Mathf.DeltaAngle(wallFacingYaw, input.LookRotation.y), -90f, 90f);
			KCC.SetLookRotation(new Vector2(input.LookRotation.x, clampedYaw), -90f, 90f);

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
			float probeLen = ClimbWallProbeDistance + ClimbStickRange;
			Vector3 origin = GetClimbProbeOrigin();

			// First, try to RE-ANCHOR to the current wall (straight + tolerant fan). This holds the player
			// on the wall they're already on — including a tick or two at a corner apex where the dead-
			// straight probe just misses — without invoking the wide corner search. That keeps the normal
			// stable (no oscillation, no flicker) and stops the apex drop.
			bool wallFound = TryReacquireCurrentWall(origin, probeLen, out RaycastHit hit);
			if (DebugClimbProbes)
				Debug.DrawRay(origin, -_climbWallNormal * probeLen, wallFound ? Color.green : Color.red, 2f);

			if (wallFound == false)
			{
				// The current wall is genuinely gone. Search wide for a wrapped corner face — but only if
				// we're not still inside a fresh wrap-commit window (belt-and-suspenders against flipping
				// back to the previous face the instant after a wrap).
				bool committed = _climbWrapCommitTimer.IsRunning && _climbWrapCommitTimer.Expired(Runner) == false;
				if (committed == false)
				{
					wallFound = TryWrapClimbCorner(input, out hit);
					if (wallFound)
						_climbWrapCommitTimer = TickTimer.CreateFromSeconds(Runner, ClimbWrapCommitDuration);
					if (DebugClimbProbes)
						Debug.Log($"[Climb] continuity MISS at {origin:F2} normal={_climbWallNormal:F2} -> wrap {(wallFound ? $"HIT {hit.collider.name} n={hit.normal:F2}" : "MISS")}");
				}
			}
			if (wallFound == false)
			{
				// Lost wall contact. If a flat ledge is right above us, kick off a mantle; otherwise drop.
				if (TryStartMantle(out Vector3 mantleEnd))
				{
					_mantleStart = transform.position;
					_mantleEnd = mantleEnd;
					_mantleTimer = TickTimer.CreateFromSeconds(Runner, MantleDuration);
					IsClimbing = false;
					_climbWallNormal = Vector3.zero;
					_climbGraceTimer = default;
					_moveVelocity = Vector3.zero;
					return false;
				}

				// No wall and no ledge this tick. Rather than dropping instantly, hold the climb for a
				// short grace window and coast with the last wall velocity, giving the player time to
				// physically round a sharp corner (where the wall isn't in front of the chest for a tick
				// or two) and let a probe reacquire. Gravity is already zeroed above, so coasting is safe.
				if (_climbGraceTimer.IsRunning == false)
					_climbGraceTimer = TickTimer.CreateFromSeconds(Runner, ClimbCornerGraceDuration);

				if (_climbGraceTimer.Expired(Runner) == false)
				{
					KCC.Move(_moveVelocity, 0f);
					return true;
				}

				// Grace expired without reacquiring a wall — genuinely off the wall now.
				ExitClimb();
				MovePlayer(Vector3.zero, 0f);
				return false;
			}
			// Reacquired (or never lost) a wall — clear the grace window and adopt the new normal.
			_climbGraceTimer = default;
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
				// Strafe axis must stay pitch-independent: deriving it from `up` (the projected look)
				// flips its sign once the look pitches below the wall horizon, inverting A/D when the
				// player looks down. surfaceRight is the stable horizontal along-wall axis.
				right = surfaceRight;
			}

			bool isMoving = input.MoveDirection.sqrMagnitude > 0.01f;
			Vector3 wallVel = (right * input.MoveDirection.x * 0.25f + up * input.MoveDirection.y) * ClimbSpeed;

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
			// Steeper walls cost more — scale the drain from 1x at ClimbForceAngle up to
			// ClimbSteepStaminaMultiplier at a vertical (90°) face, so hauling up an overhang-y wall
			// burns faster than a barely-too-steep ramp.
			float wallAngle = Vector3.Angle(_climbWallNormal, Vector3.up);
			float steepT = ClimbForceAngle < 90f
				? Mathf.Clamp01(Mathf.InverseLerp(ClimbForceAngle, 90f, wallAngle))
				: 0f;
			float angleMultiplier = Mathf.Lerp(1f, ClimbSteepStaminaMultiplier, steepT);
			float drainRate = (isMoving ? ClimbStaminaMoveDrain : ClimbStaminaIdleDrain) * angleMultiplier;
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
			_climbGraceTimer = default;
			_climbWrapCommitTimer = default;
			_moveVelocity = Vector3.zero;
		}

		// Horizontal yaw offsets (degrees) for the corner fan-sweep, ordered nearest-to-straight first so
		// the most continuous surface wins. Both signs are tried so the wall can turn either way. The
		// camera's climb yaw is clamped to ±90° from the wall facing, so a span to ±110° comfortably
		// covers any 90° corner regardless of where the player is looking.
		private static readonly float[] _cornerSweepAngles = { 30f, -30f, 55f, -55f, 80f, -80f, 105f, -105f };

		// Small horizontal fan used to RE-ANCHOR to the current wall when the dead-straight probe just
		// misses (player drifted slightly off-axis at the apex). Tolerant enough to hold the wall without
		// dropping, but narrow enough — combined with the normal-deviation gate — to never grab the
		// perpendicular face of a corner (that's the wide-search wrap's job).
		private static readonly float[] _continuityFanAngles = { 20f, -20f, 40f, -40f };

		// Re-anchor to the CURRENT wall: dead-straight probe along -normal, then a small fan, accepting a
		// fan hit only if its face stays within ~55° of the current normal. Holding the current wall this
		// way (instead of bare straight-only) stops the player from dropping at a corner apex, and the
		// angle gate keeps it from latching the perpendicular face (which would oscillate).
		private bool TryReacquireCurrentWall(Vector3 origin, float probeLen, out RaycastHit hit)
		{
			Vector3 baseDir = -_climbWallNormal;
			if (Physics.Raycast(origin, baseDir, out hit, probeLen, ClimbableMask, QueryTriggerInteraction.Ignore))
				return true; // exact straight hit — same (or continuously curving) wall

			Vector3 intoH = new Vector3(baseDir.x, 0f, baseDir.z);
			if (intoH.sqrMagnitude < 0.0001f) return false;
			intoH.Normalize();
			foreach (float ang in _continuityFanAngles)
			{
				Vector3 d = Quaternion.AngleAxis(ang, Vector3.up) * intoH;
				if (Physics.Raycast(origin, d, out hit, probeLen, ClimbableMask, QueryTriggerInteraction.Ignore)
					&& Vector3.Angle(hit.normal, _climbWallNormal) <= 55f)
					return true;
			}
			hit = default;
			return false;
		}

		// When the straight wall re-probe (locked to the old wall normal) misses, the player may be
		// wrapping around a vertical corner rather than leaving the wall. Find where the surface continues
		// using a purely GEOMETRIC search — no dependency on camera look or movement direction, so it
		// works the same whether the player faces the wall, looks away, or stands still at the edge:
		//   1. Concave / turned faces: fan the into-wall probe left and right around vertical (the old
		//      face simply bent away); the first steep climbable hit wins.
		//   2. Convex / outer edges: step the origin past the edge along each horizontal tangent, then
		//      cast back to catch the perpendicular face wrapped around the corner.
		// On a hit, `wall` is the new anchor face; the per-tick anchor correction in ProcessClimbInput
		// then snaps the player to its standoff distance, completing the wrap.
		private bool TryWrapClimbCorner(GameplayInput input, out RaycastHit wall)
		{
			wall = default;
			Vector3 origin = GetClimbProbeOrigin();
			float reach = ClimbWallProbeDistance + ClimbStickRange;

			// Horizontal "into the wall" direction (drop any vertical tilt) and the horizontal tangent.
			Vector3 into = -_climbWallNormal;
			into.y = 0f;
			if (into.sqrMagnitude < 0.0001f) return false; // wall is a ceiling/floor — nothing to wrap to
			into.Normalize();
			Vector3 tangent = Vector3.Cross(Vector3.up, into).normalized;

			// 1) Concave fan: rotate the into-wall probe around vertical to catch a face that turned away.
			foreach (float ang in _cornerSweepAngles)
			{
				Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * into;
				if (DebugClimbProbes) Debug.DrawRay(origin, dir * reach, Color.magenta, 2f);
				if (Physics.Raycast(origin, dir, out wall, reach, ClimbableMask, QueryTriggerInteraction.Ignore)
					&& Vector3.Angle(wall.normal, Vector3.up) >= ClimbForceAngle)
					return true;
			}

			// 2) Convex sweep: step past the edge along each tangent and cast back for the wrapped face.
			for (int s = -1; s <= 1; s += 2)
			{
				Vector3 step = tangent * s;
				Vector3 convexOrigin = origin + step * reach;
				if (DebugClimbProbes) Debug.DrawRay(convexOrigin, -step * (reach + ClimbStickRange), Color.yellow, 2f);
				if (Physics.Raycast(convexOrigin, -step, out wall, reach + ClimbStickRange, ClimbableMask, QueryTriggerInteraction.Ignore)
					&& Vector3.Angle(wall.normal, Vector3.up) >= ClimbForceAngle)
					return true;
			}

			return false;
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

		/// <summary>True while an emote is playing. Blocks movement and attack input.</summary>
		public bool IsEmoting { get; private set; }

		/// <summary>Called by PlayerEmotes to network the emote animation trigger to all clients.</summary>
		public void TriggerEmoteForAll(int emoteIndex)
		{
			RpcTriggerEmote((byte)emoteIndex);
		}

		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
		private void RpcTriggerEmote(byte emoteIndex)
		{
			_emoteIndex = emoteIndex;
			_emoteCount++;
		}

		private void OnEmoteCountChanged()
		{
			var emotes = GetComponent<PlayerEmotes>()?.Emotes;
			if (emotes == null || _emoteIndex >= emotes.Length) return;
			var emote = emotes[_emoteIndex];
			if (emote == null) return;
			BodyAnimator?  .SetTrigger(emote.TriggerName);
			NoHeadAnimator?.SetTrigger(emote.TriggerName);
		}

		/// <summary>
		/// Called by PlayerEmotes to play emote audio.
		/// Plays locally immediately, then RPC to Others so the sender doesn't hear it twice.
		/// </summary>
		public void PlayEmoteAudioForAll(int emoteIndex, float delay)
		{
			// Play locally on the emoting client directly.
			var emotes = GetComponent<PlayerEmotes>()?.Emotes;
			if (emotes != null && emoteIndex >= 0 && emoteIndex < emotes.Length)
			{
				var emote = emotes[emoteIndex];
				if (emote?.EmoteAudio != null)
					StartCoroutine(PlayEmoteAudioCoroutine(emote.EmoteAudio, transform.position, emote.AudioVolume, delay));
			}
			// Tell all OTHER clients to also play it.
			RpcPlayEmoteAudio(emoteIndex, delay);
		}

		[Rpc(RpcSources.InputAuthority, RpcTargets.All)]
		private void RpcPlayEmoteAudio(int emoteIndex, float delay)
		{
			// Sender already played it locally in PlayEmoteAudioForAll — skip to avoid double play.
			if (HasInputAuthority) return;
			var emotes = GetComponent<PlayerEmotes>()?.Emotes;
			if (emotes == null || emoteIndex < 0 || emoteIndex >= emotes.Length) return;
			var emote = emotes[emoteIndex];
			if (emote == null || emote.EmoteAudio == null) return;
			StartCoroutine(PlayEmoteAudioCoroutine(emote.EmoteAudio, transform.position, emote.AudioVolume, delay));
		}

		private System.Collections.IEnumerator PlayEmoteAudioCoroutine(AudioClip clip, Vector3 pos, float volume, float delay)
		{
			if (delay > 0f) yield return new WaitForSeconds(delay);
			AudioManager.Instance?.PlaySFX(clip, pos, volume);
		}

		/// <summary>
		/// Local-space camera offset added during a ShowFullBody emote.
		/// PlayerEmotes lerps this toward ThirdPersonOffset then back to zero.
		/// </summary>
		public Vector3 EmoteCameraOffset { get; set; }

		/// <summary>
		/// When true, PlayerFullBody is shown for the local player (overriding the normal NoHead-only view).
		/// PlayerNoHead is hidden so only the full body is visible.
		/// </summary>
		public bool ShowFullBodyForEmote { get; set; }

		/// <summary>
		/// Called by PlayerEmotes to suppress arm IK while an emote plays and lock player input.
		/// Pass false when the emote ends to restore normal behaviour.
		/// </summary>
		public void SetIKSuppressedByEmote(bool suppressed)
		{
			_ikSuppressedByEmote = suppressed;
			IsEmoting            = suppressed;
		}

		/// <summary>Fires an Animator trigger on both BodyAnimator and NoHeadAnimator simultaneously.</summary>
		[Sirenix.OdinInspector.Button]
		public void TriggerAnimation(string triggerName)
		{
			if (string.IsNullOrEmpty(triggerName)) return;
			// Unity-safe null checks: the `?.` operator bypasses Unity's overloaded == and
			// throws UnassignedReferenceException on a serialized-but-unassigned field.
			if (BodyAnimator   != null) BodyAnimator.SetTrigger(triggerName);
			if (NoHeadAnimator != null) NoHeadAnimator.SetTrigger(triggerName);
		}

		/// <summary>Drive a body animator with the current movement/climb state.</summary>
		private void DriveBodyAnimator(Animator anim, float horizontalSpeed)
		{
			if (anim == null) return;
			if (IsClimbing)
			{
				anim.SetBool (_bodyAnimIDIsClimbing,  true);
				anim.SetBool (_bodyAnimIDFreeFall,    false);
				anim.SetBool (_bodyAnimIDJump,        false);
				anim.SetBool (_bodyAnimIDGrounded,    false);
				anim.SetFloat(_bodyAnimIDSpeed,       0f, 0.1f, Time.deltaTime);
				anim.SetFloat(_bodyAnimIDMotionSpeed, 1f);
				anim.speed = KCC.RealVelocity.magnitude > 0.1f ? 1f : 0f;
			}
			else
			{
				anim.speed = 1f;
				anim.SetBool (_bodyAnimIDIsClimbing,  false);
				anim.SetFloat(_bodyAnimIDSpeed,       horizontalSpeed, 0.1f, Time.deltaTime);
				anim.SetFloat(_bodyAnimIDMotionSpeed, 1f);
				anim.SetBool (_bodyAnimIDGrounded,    KCC.IsGrounded);
				anim.SetBool (_bodyAnimIDFreeFall,    !KCC.IsGrounded && KCC.RealVelocity.y < -1f);
				anim.SetBool (_bodyAnimIDJump,        _isJumping);
			}
		}

		/// <summary>Set world rotation on a body animator, or reset to inherited when not climbing.</summary>
		private void ApplyClimbRotation(Animator anim)
		{
			if (anim == null) return;
			if (IsClimbing)
			{
				Vector3 faceDir = new Vector3(-_climbWallNormal.x, 0f, -_climbWallNormal.z);
				if (faceDir.sqrMagnitude > 0.001f)
				{
					// Ease toward the wall-facing rotation rather than snapping. The corner-wrap turn is a
					// one-shot ~90° change; smoothing it (frame-rate-independent) keeps the body from
					// popping as the normal flips between faces. Commit-locking already stops oscillation;
					// this just polishes the single turn.
					Quaternion target = Quaternion.LookRotation(faceDir, Vector3.up);
					float t = 1f - Mathf.Exp(-14f * Time.deltaTime);
					anim.transform.rotation = Quaternion.Slerp(anim.transform.rotation, target, t);
				}
			}
			else
			{
				anim.transform.localRotation = Quaternion.identity;
			}
		}

		private Vector3 GetClimbProbeOrigin()
		{
			return ChestBone != null ? ChestBone.position : transform.position + Vector3.up * 1.0f;
		}

		// Yaw-only forward probe for a surface too steep to walk (steeper than ClimbForceAngle). If the
		// desired velocity drives into such a face, removes the into-surface horizontal component so the
		// player can't creep up it. Unlike the climb probe this hits ALL geometry (not just ClimbableMask),
		// so non-climbable steep faces become hard barriers. Gentle ramps (below ClimbForceAngle) and low
		// steps (below the chest probe) pass through untouched.
		private Vector3 BlockSteepSlopeMovement(Vector3 desiredVel)
		{
			Vector3 horizDir = desiredVel;
			horizDir.y = 0f;
			if (horizDir.sqrMagnitude < 0.0001f) return desiredVel;
			horizDir.Normalize();

			Vector3 origin = GetClimbProbeOrigin();
			float probe = KCC.Settings.Radius + 0.2f;
			int mask = Physics.DefaultRaycastLayers & ~(1 << gameObject.layer);
			if (Physics.Raycast(origin, horizDir, out RaycastHit hit, probe, mask, QueryTriggerInteraction.Ignore) == false)
				return desiredVel;

			if (Vector3.Angle(hit.normal, Vector3.up) < ClimbForceAngle) return desiredVel;

			Vector3 normalHoriz = hit.normal;
			normalHoriz.y = 0f;
			if (normalHoriz.sqrMagnitude < 0.0001f) return desiredVel; // near-vertical — KCC already walls it
			normalHoriz.Normalize();

			float into = Vector3.Dot(desiredVel, -normalHoriz);
			if (into > 0f) desiredVel += normalHoriz * into; // cancel the into-surface component
			return desiredVel;
		}

		// Drives the KCC capsule height from the networked IsCrouching/_isJumping state. Called from every path
		// that touches IsCrouching so each peer's capsule matches the replicated state without an OnChangedRender.
		// IsDowned overrides to a shorter DownedHeight so the body reads as flat on the floor.
		// While airborne (_isJumping) the capsule shrinks to CrouchHeight so the player can pass
		// through windows and low openings without needing to hold crouch mid-air.
		private void ApplyCrouchHeight()
		{
			if (_standHeight <= 0f) return;
			float target;
			if (IsDowned) target = DownedHeight;
			else if (IsCrouching || _isJumping) target = CrouchHeight;
			else target = _standHeight;
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

		// Tracks the player's fall speed and applies damage on the airborne→grounded transition. State-authority
		// only — fall damage is unattributed environmental damage (Health.TakeHit(int) → PlayerRef.None), so it
		// bypasses the PvP phase/team gate and lands in any phase. Suppressed while climbing, mantling, grappling,
		// seated, sleeping, downed, ragdolled, or dead — none of those count as a free fall.
		private void ProcessFallDamage()
		{
			if (HasStateAuthority == false || EnableFallDamage == false) return;

			bool tracking = Health.IsAlive
				&& RagdollState == ERagdollState.Normal
				&& IsClimbing == false && IsMantling == false && IsGrappling == false
				&& IsSeated == false && IsSleeping == false && IsDowned == false;

			if (tracking == false)
			{
				// In a non-falling state — forget any accumulated fall and don't register a landing edge.
				_fallPeakDownSpeed = 0f;
				_wasGroundedForFall = KCC.IsGrounded;
				return;
			}

			bool grounded = KCC.IsGrounded;
			if (grounded == false)
			{
				// Airborne: remember the fastest descent speed seen this fall.
				float downSpeed = -KCC.RealVelocity.y;
				if (downSpeed > _fallPeakDownSpeed)
					_fallPeakDownSpeed = downSpeed;
			}
			else
			{
				// Just touched down? Convert peak descent speed into damage above the safe threshold.
				if (_wasGroundedForFall == false && _fallPeakDownSpeed > SafeFallSpeed && LethalFallSpeed > SafeFallSpeed)
				{
					float t = Mathf.InverseLerp(SafeFallSpeed, LethalFallSpeed, _fallPeakDownSpeed);
					int damage = Mathf.Max(1, Mathf.RoundToInt(t * MaxFallDamage));
					Health.TakeHit(damage);
				}
				_fallPeakDownSpeed = 0f;
			}

			_wasGroundedForFall = grounded;
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

			// While reeling, kill gravity so the pull tracks straight to the anchor (incl. upward) instead of
			// sagging — the reel velocity is the sole vertical driver until arrival/detach. Suppressed while
			// climbing/mantling, which call MovePlayer on their own path (no UpdateGrappleTick) — the grapple is
			// detached on entering those states anyway, this just guards the one-tick overlap.
			bool reeling = IsGrappling && IsClimbing == false;
			if (reeling) KCC.SetGravity(0f);

			Vector3 grappleVelocity = reeling ? ComputeGrappleVelocity() : Vector3.zero;
			KCC.Move(_moveVelocity + ComputeKnockbackVelocity() + ComputeRagdollRollVelocity() + grappleVelocity + ComputePlayerPushVelocity(), jumpImpulse);
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

		/// <summary>
		/// Gentle mutual separation from other living players/bots so nobody can hard-block a doorway / entrance:
		/// each player nudges itself away from bodies inside <see cref="PlayerPushRadius"/>, stronger the closer
		/// they are. Symmetric (both sides push apart), so walking into someone slides them aside while they slide
		/// you. Derived purely from networked positions, so every predicting peer computes the same nudge; folded
		/// into <see cref="KCC.Move"/> next to knockback. Cosmetic-magnitude — never an attack.
		/// </summary>
		private Vector3 ComputePlayerPushVelocity()
		{
			if (PlayerPushRadius <= 0f || PlayerPushStrength <= 0f) return Vector3.zero;
			// Only an upright, in-control body pushes: skip while dead, ragdolled, downed, or seated in a vehicle.
			if (Health == null || Health.IsAlive == false) return Vector3.zero;
			if (RagdollState != ERagdollState.Normal || IsDowned || IsSeated) return Vector3.zero;

			Vector3 myPos = KCC.Position;
			float radius = PlayerPushRadius;
			float radiusSqr = radius * radius;
			Vector3 push = Vector3.zero;

			for (int i = 0; i < All.Count; i++)
			{
				var other = All[i];
				if (other == null || other == this || other.Object == null) continue;
				if (other.Health == null || other.Health.IsAlive == false || other.IsSeated) continue;

				Vector3 away = myPos - other.KCC.Position;
				away.y = 0f;
				float distSqr = away.sqrMagnitude;
				if (distSqr >= radiusSqr) continue;

				if (distSqr < 1e-4f)
				{
					// Bodies are exactly stacked — separate along a deterministic per-pair axis so the two peers
					// agree on opposite directions instead of jittering. PlayerId tiebreak keeps it stable.
					push += new Vector3(Owner.PlayerId > other.Owner.PlayerId ? 1f : -1f, 0f, 0f);
					continue;
				}

				float dist = Mathf.Sqrt(distSqr);
				push += (away / dist) * (1f - dist / radius); // closer → stronger
			}

			if (push.sqrMagnitude < 1e-6f) return Vector3.zero;
			return Vector3.ClampMagnitude(push, 1f) * PlayerPushStrength;
		}

		private void Fire(CombatAction action, bool charged, float chargeNormalized = 0f)
		{
			if (action == null || ActionInvoker == null) return;
			// Authoritative safety net behind the input gate. AllowInPeacePhase actions (the punch) skip it so
			// they fire in town; their damage is still gated by Health's PvP rules, only the shove lands.
			if (action.AllowInPeacePhase == false && CanFireWeaponNow() == false) return;

			// Magazine gate (hitscan guns): a dry or reloading magazine produces a click, no shot.
			// Checked on every predicting peer — Slots.Loaded / IsReloading are networked so IA and SA
			// agree without an RPC, mirroring ProjectileAction's ammo gate.
			var weapon = _inventory != null ? _inventory.ActiveWeapon : null;
			bool magazineFed = weapon != null && weapon.UsesMagazine;
			if (magazineFed && (_inventory.IsReloading || _inventory.ActiveLoaded <= 0))
				return;

			// Clear hit position in case nothing will be hit
			_hitPosition = Vector3.zero;
			_hitNormal = Vector3.zero;

			var ctx = new ActorContext
			{
				Runner = Runner,
				IgnoreAuthority = Owner,
				AttackerPosition = transform.position,
				FireTransform = CameraHandle,
				AttackerRoot = gameObject,
				IsStateAuthority = HasStateAuthority,
				ChargeNormalized = chargeNormalized,
				IsAiming = IsAiming,
			};

			var hit = ActionInvoker.TryFire(action, in ctx, charged);
			if (hit.DidFire == false) return;

			// Consume one magazine round only on a shot that actually fired (so a cooldown-rejected
			// press doesn't waste ammo). Predicted on IA via the networked Slots write.
			if (magazineFed) _inventory.TryConsumeChamberedRound();

			if (hit.DidHit)
			{
				_hitPosition = hit.Point;
				_hitNormal = hit.Normal;
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
				var visual = GetHeldVisual();
				var action = GetActiveAction();

				if (visual != null && action != null)
				{
					visual.PlayAttackSound(action);
					if (action.Style == EFeedbackStyle.Melee)
					{
						visual.PlayMeleeFeedback(ActionInvoker != null && ActionInvoker.LastFireWasCharged);
					}
					else
					{
						visual.PlayRangedFeedback();
					}
				}

				if (Animator != null) Animator.SetTrigger(_animIDShoot);

				if (_hitPosition != Vector3.zero)
				{
					// Per-action impact VFX (e.g. pistol spark, bat dust) overrides the
					// global default; both are local-only and self-destruct via DestroyAfter.
					var impact = action != null && action.ImpactEffect != null ? action.ImpactEffect : ImpactPrefab;
					if (impact != null)
					{
						// Overlap/melee hits leave the normal zero — fall back to identity so
						// LookRotation doesn't spam "zero viewing vector" warnings.
						var rot = _hitNormal != Vector3.zero ? Quaternion.LookRotation(_hitNormal) : Quaternion.identity;
						Instantiate(impact, _hitPosition, rot);
					}
				}
			}

			_visibleFireCount = _fireCount;

			// Secondary (throw) feedback: best-effort swing on the held visual while it still exists.
			// The thrown projectile carries the authoritative cross-peer cue, so a consumed/unequipped
			// weapon simply skips the local swing.
			if (_visibleSecondaryFireCount < _secondaryFireCount)
			{
				GetHeldVisual()?.PlayMeleeFeedback(false);
				if (Animator != null) Animator.SetTrigger(_animIDShoot);
			}
			_visibleSecondaryFireCount = _secondaryFireCount;
		}

		// Feedback sink: the spawned hand prefab's visual rig (weapon swing/recoil, fist punch).
		private IHeldVisual GetHeldVisual()
		{
			if (_inventory == null) return null;
			var instance = _inventory.HeldInstance;
			return instance != null ? instance.GetComponent<IHeldVisual>() : null;
		}

		// Active action is sourced from the item asset, not the prefab (see Inventory.ActiveAction):
		// the selected item's WeaponCapability, or the unarmed (fists) action when the hand is empty.
		// A non-weapon held item (medkit, wood, ...) yields null, so it can't attack.
		private CombatAction GetActiveAction() => _inventory != null ? _inventory.ActiveAction : null;

		/// <summary>State-authority-only setter for <see cref="PvpArmed"/>. Called by <see cref="ZoneManager"/>
		/// when the player enters their team zone (arm) or at the start of a new Night / Lobby (disarm).</summary>
		public void SetPvpArmed(bool armed)
		{
			if (HasStateAuthority == false) return;
			if (PvpArmed != armed) PvpArmed = armed;
		}

		/// <summary>Weapons can only fire during Night, and only once the player has armed by reaching their
		/// team's zone (see <see cref="ZoneManager"/>). During Day / Lobby / Dusk / MatchOver no action fires at
		/// all, which prevents the Night opening from becoming an instant cluster-kill.
		///
		/// When the scene has no <see cref="MatchManager"/> yet (the current <c>03_Shooter</c> scene — match flow
		/// is still planned) we fall back to the visual day/night clock (<see cref="TimeManager"/>): weapons fire
		/// only at night. A scene with neither manager (isolated test scenes) stays ungated.</summary>
		private bool CanFireWeaponNow()
		{
			var match = MatchManager.Instance;
			if (match != null)
			{
				if (match.IsPvpForced) return true; // `arm` debug override — fire regardless of phase / zone-arming
				if (match.Phase != MatchPhase.Night) return false;
				return PvpArmed;
			}

			// No MatchManager: gate on the day/night cycle instead so LMB/weapon fire is dead during daytime.
			var time = TimeManager.Instance;
			if (time != null) return time.IsNight;
			return true;
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
				if (JumpAudioClip != null)
					AudioSource.PlayClipAtPoint(JumpAudioClip, KCC.Position, 0.5f);
				else
					Debug.LogWarning("Player.JumpAudioClip is not assigned; skipping jump sound.", this);
			}
			else
			{
				if (LandAudioClip != null)
					AudioSource.PlayClipAtPoint(LandAudioClip, KCC.Position, 1f);
				else
					Debug.LogWarning("Player.LandAudioClip is not assigned; skipping land sound.", this);
			}

			if (HasInputAuthority == false)
			{
				ScalingRoot.localScale = _isJumping ? new Vector3(0.5f, 1.5f, 0.5f) : new Vector3(1.25f, 0.75f, 1.25f);
			}
		}

		// Fires on every peer when the host bumps SpawnLookVersion (each Respawn). Only the input authority needs to
		// act: re-seed its accumulated look INPUT so the next ticks aim at the spawn yaw (without this, the input
		// pipeline overrides the host's KCC look on tick 1), plus set the KCC look now to avoid a one-frame flash.
		private void OnSpawnLookChanged()
		{
			if (HasInputAuthority == false)
				return;

			KCC.SetLookRotation(0f, SpawnLookYaw);
			GetComponent<PlayerInput>()?.SetLookRotation(new Vector2(0f, SpawnLookYaw));
		}

		// Drives the colour of the remote player's nameplate relation circle. Friendly (green) = same team as the
		// local player; otherwise neutral (blue) during Town/Lobby and hostile (red) once the Purge (Night) begins —
		// every non-ally is fair game at night. Local player has no nameplate, so this is a no-op for them.
		private void UpdateNameplateRelation()
		{
			if (Nameplate == null || HasInputAuthority)
				return;
			if (Nameplate.gameObject.activeSelf == false)
				return; // Nameplate only switches on once the nickname arrives (OnNicknameChanged).

			Nameplate.SetRelation(ResolveRelation());
		}

		private UINameplate.NameplateRelation ResolveRelation()
		{
			var local = Runner != null ? Runner.LocalPlayer : PlayerRef.None;
			var teams = TeamManager.Instance;
			if (teams != null && local != PlayerRef.None && teams.SameTeam(local, Owner))
				return UINameplate.NameplateRelation.Friendly;

			// Non-ally: hostile once PvP is live (Night, or the debug 'arm' override), neutral otherwise.
			var match = MatchManager.Instance;
			bool pvpLive = match != null && (match.IsPvpActive || match.IsPvpForced);
			return pvpLive ? UINameplate.NameplateRelation.Hostile : UINameplate.NameplateRelation.Neutral;
		}

		private void OnNicknameChanged()
		{
			if (HasInputAuthority)
				return; // Do not show nickname for local player

			// The Nameplate GameObject ships INACTIVE on the prefab (so the local player never renders one).
			// Every remote view — bots and other humans alike — has to switch it on, otherwise the indicator
			// never appears no matter how the nickname is set.
			if (Nameplate != null)
			{
				Nameplate.gameObject.SetActive(true);
				Nameplate.SetNickname(Nickname);
				Nameplate.SetRelation(ResolveRelation()); // correct colour from the first frame
			}
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

		// ---------------- Elimination banner (local view) ----------------

		/// <summary>Display name of the most recent player this client eliminated. Set by
		/// <see cref="RPC_NotifyElimination"/> (host → killer), read by <see cref="UIShooter"/> to flash a
		/// centre-screen "Eliminated &lt;name&gt;" banner. Local-only view state — never networked.</summary>
		[System.NonSerialized] public string LastEliminatedName;

		/// <summary>Unscaled time the last elimination banner was triggered. UIShooter holds then fades the
		/// banner from this stamp. Large negative sentinel = nothing to show yet.</summary>
		[System.NonSerialized] public float LastEliminationTime = -999f;

		/// <summary>Host → killer notification that the local player just eliminated another player. Fired from
		/// <see cref="Health.TakeHit"/> on the state authority against the attacker's Player object, so it lands
		/// only on the killer's client. Stamps local banner state — no networked mutation here.</summary>
		[Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
		public void RPC_NotifyElimination(string victimName)
		{
			LastEliminatedName = victimName;
			LastEliminationTime = Time.unscaledTime;
		}

		// ---------------- Downed / revive ----------------

		/// <summary>State-authority-only: bleed-out + revive accrual for an already-downed player.
		/// While at least one ally is reviving (<see cref="ReviveCount"/> &gt; 0) the bleed-out timer
		/// pauses and <see cref="ReviveProgress"/> advances; when it hits 1 the player is revived
		/// in-place. With no reviver the timer ticks down; on 0 the player actually dies via
		/// <see cref="Health.AuthorityKill"/> so the normal death/ragdoll/respawn flow runs.</summary>
		private void UpdateDownedAuthority()
		{
			if (HasStateAuthority == false) return;
			if (IsDowned == false) return;

			// A downed player can still take a second lethal hit: Health.TakeHit drops HP straight to 0
			// because OnLethalDamage declines the down-hook once we're already downed. That leaves us
			// downed AND dead at once — the crawl early-return in FixedUpdateNetwork would keep running
			// ProcessDownedTick on a corpse while RagdollState is Dead, so the body half-responds to input
			// instead of dying cleanly. Clear the downed state so CheckDeathRagdoll's Dead transition wins
			// and the normal frozen-corpse flow runs.
			if (Health.IsAlive == false)
			{
				IsDowned = false;
				DownedTimeRemaining = 0f;
				ReviveCount = 0;
				ReviveProgress = 0f;
				return;
			}

			if (ReviveCount > 0)
			{
				float duration = Mathf.Max(0.01f, ReviveHoldSeconds);
				ReviveProgress = Mathf.Clamp01(ReviveProgress + Runner.DeltaTime / duration);
				if (ReviveProgress >= 1f)
				{
					int target = Mathf.Max(1, Mathf.Min(Health.InitialHealth, ReviveHealth));
					Health.CurrentHealth = target;
					IsDowned = false;
					DownedTimeRemaining = 0f;
					ReviveCount = 0;
					ReviveProgress = 0f;
				}
			}
			else
			{
				DownedTimeRemaining = Mathf.Max(0f, DownedTimeRemaining - Runner.DeltaTime);
				if (DownedTimeRemaining <= 0f)
				{
					IsDowned = false;
					DownedTimeRemaining = 0f;
					ReviveCount = 0;
					ReviveProgress = 0f;
					Health.AuthorityKill();
				}
			}
		}

		/// <summary>Per-tick crawl handling while <see cref="IsDowned"/>. Look stays free, slow
		/// WASD crawl, all action buttons (fire/jump/sprint/charge) are ignored. Forces IsCrouching
		/// + DownedHeight via <see cref="ApplyCrouchHeight"/> so the capsule reads as on the floor.</summary>
		private void ProcessDownedTick()
		{
			// Force the crouched flag so proxies see the short capsule too. ApplyCrouchHeight
			// overrides to DownedHeight when IsDowned, so the actual KCC height is even lower
			// than a normal crouch — IsCrouching is just the convenient replicated signal.
			if (HasStateAuthority && IsCrouching == false)
			{
				IsCrouching = true;
			}
			ApplyCrouchHeight();

			if (HasStateAuthority)
			{
				if (IsClimbing) ExitClimb();
				if (ActionInvoker != null && ActionInvoker.IsCharging) ActionInvoker.CancelCharge();
			}

			if (TryGetTickInput(out var input, out _))
			{
				KCC.SetLookRotation(input.LookRotation, -90f, 90f);

				Vector3 moveDirection = KCC.TransformRotation * new Vector3(input.MoveDirection.x, 0f, input.MoveDirection.y);
				MovePlayer(moveDirection * DownedCrawlSpeed, 0f);

				// Match ProcessInput so the camera handle / fire transform stays on the same pivot.
				// Fire is suppressed by PlayerInput anyway, but keeping the pivot in sync avoids any
				// stale rotation reads (e.g. by ShowFireEffects if a fire was queued earlier).
				var pitchRotation = KCC.GetLookRotation(true, false);
				CameraPivot.localRotation = Quaternion.Euler(pitchRotation);
			}
			else
			{
				MovePlayer(Vector3.zero, 0f);
			}

			_wasSprinting = false;
			_isJumping = false;
		}

		/// <summary>Wired into <see cref="Health.AuthorityDownHook"/> on <see cref="Spawned"/>. Fires
		/// on the state authority just before HP would zero out — if we're not already downed AND a
		/// teammate could still revive us, absorb the lethal blow and enter the downed state. Returning
		/// false lets the kill go through normally (chained hit while already downed = real death; Solo
		/// or a wiped-out team has no possible reviver, so dying outright avoids a bleed-out nobody can
		/// interrupt; sleeping invulnerability is enforced earlier by Health.IsInvulnerable so we don't
		/// gate on it here).</summary>
		private bool OnLethalDamage()
		{
			if (HasStateAuthority == false) return false;
			if (IsDowned) return false;

			// No teammate who could ever revive us (Solo team, or every ally already dead/downed) →
			// die outright instead of crawling out a bleed-out nobody can interrupt.
			if (HasRevivableAlly() == false) return false;

			IsDowned = true;
			DownedTimeRemaining = DownedBleedOutSeconds;
			ReviveCount = 0;
			ReviveProgress = 0f;

			if (IsClimbing) ExitClimb();
			if (ActionInvoker != null && ActionInvoker.IsCharging) ActionInvoker.CancelCharge();
			_isJumping = false;
			_wasSprinting = false;

			return true;
		}

		/// <summary>State-authority helper: true when at least one same-team player is alive and not
		/// themselves downed — i.e. someone who could actually walk over and revive us. A Solo team
		/// (size 1) never has one, so Solo deaths are always instant; in Duo/Trio a player whose
		/// teammates are all dead or downed also dies outright rather than entering a futile bleed-out.
		/// Keys on <see cref="Owner"/> (synthetic for bots) so it matches the team-assignment ref.</summary>
		private bool HasRevivableAlly()
		{
			var team = TeamManager.Instance;
			if (team == null) return false; // No team system (shouldn't happen mid-match) → no reviver.

			for (int i = 0; i < All.Count; i++)
			{
				var other = All[i];
				if (other == null || other == this) continue;
				if (other.Health == null || other.Health.IsAlive == false) continue;
				if (other.IsDowned) continue; // A downed ally can't revive anyone.
				if (team.SameTeam(Owner, other.Owner) == false) continue;
				return true;
			}

			return false;
		}

		/// <summary>Adds the floating revive prompt to the player root. <see cref="InteractionPrompt.HideWhenLocked"/>
		/// gates the canvas on <c>IInteractable.CanInteract</c>, which the downed Player flips with
		/// <see cref="IsDowned"/> — so alive players don't carry a glowing indicator.</summary>
		private void SetupDownedInteractionPrompt()
		{
			if (GetComponent<InteractionPrompt>() != null) return;

			var prompt = gameObject.AddComponent<InteractionPrompt>();
			prompt.HideWhenLocked = true;
			prompt.LocalOffset = new Vector3(0f, 0.6f, 0f);
		}

		private bool IsReviverInRange(PlayerRef reviver)
		{
			if (Runner == null) return false;
			var obj = Runner.GetPlayerObject(reviver);
			if (obj == null) return false;
			float allowed = ReviveInteractRange * 1.25f;
			return (obj.transform.position - transform.position).sqrMagnitude <= allowed * allowed;
		}

		private void OnIsDownedChanged()
		{
			// Kept as a hook for SFX / animator triggers. Capsule height + camera drop are driven
			// every tick from ApplyCrouchHeight / LateUpdate, so no per-edge work is needed yet.
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_BeginRevive(RpcInfo info = default)
		{
			if (IsDowned == false) return;

			var source = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
			if (source == Object.InputAuthority) return;
			if (IsReviverInRange(source) == false) return;

			ReviveCount++;
			// Don't reset ReviveProgress — a new reviver joining mid-hold should pick up where
			// the previous one left off (still has to add up to ReviveHoldSeconds total).
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_EndRevive(RpcInfo info = default)
		{
			if (ReviveCount > 0) ReviveCount--;
			if (ReviveCount == 0)
			{
				// Last reviver abandoned — reset progress so a fresh attempt restarts the hold.
				ReviveProgress = 0f;
			}
		}

		// --- IInteractable (downed-only revive target) ---

		float IInteractable.InteractRange => ReviveInteractRange;
		bool IInteractable.CanInteract => IsDowned;
		Vector3 IInteractable.InteractionPoint => transform.position;
		string IInteractable.LockedReason => string.Empty;
		string IInteractable.InteractLabel => ReviveInteractLabel;
		void IInteractable.OnInteract(InteractionScanner scanner)
		{
			// Revive is hold-only — the tap path is reachable when the scanner can't engage hold
			// (shouldn't happen since CanInteract gates this), so leave empty rather than half-revive.
		}

		// --- IHoldInteractable ---

		float IHoldInteractable.HoldSeconds => ReviveHoldSeconds;
		void IHoldInteractable.LocalRequestHoldStart() => RPC_BeginRevive();
		void IHoldInteractable.LocalRequestHoldCancel() => RPC_EndRevive();
		void IHoldInteractable.LocalRequestHoldComplete()
		{
			// No-op by design. State authority watches ReviveProgress in UpdateDownedAuthority
			// and finishes the revive when it crosses 1.0; the reviver's local hold completion
			// just signals the scanner to stop ticking. The Begin RPC remains in effect — its
			// matching End is sent by AbortHold if needed (here it isn't, scanner cleared refs).
		}

		// --- IInteractionGate ---

		// Block all interaction while downed, and during the pre-round wait (game scene still in Lobby, waiting for
		// every player to load in). Players can move within their spawn area during that wait but can't interact until
		// the round actually begins. See MatchManager.RoundStarted and ApplySpawnConfinement.
		bool IInteractionGate.AllowInteractions => IsDowned == false && MatchManager.RoundStarted;

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
			if (Animator != null)
			{
				Animator.SetFloat(_animIDSpeedX, 0f, 0.1f, Time.deltaTime);
				Animator.SetFloat(_animIDSpeedZ, 0f, 0.1f, Time.deltaTime);
				Animator.SetBool(_animIDGrounded, true);
			}
			foreach (var anim in new[] { BodyAnimator, NoHeadAnimator })
			{
				if (anim == null) continue;
				anim.SetFloat(_bodyAnimIDSpeed,       0f, 0.1f, Time.deltaTime);
				anim.SetFloat(_bodyAnimIDMotionSpeed, 1f);
				anim.SetBool (_bodyAnimIDGrounded,    true);
				anim.SetBool (_bodyAnimIDFreeFall,    false);
				anim.SetBool (_bodyAnimIDJump,        false);
			}
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

			if (Camera.main != null)
			{
				var seat = InCurrentSeat;
				// Preferred: drive the camera position from an authored Transform inside the cab
				// (seat.CameraMount). Fallback to a yaw-only offset from the player root (which
				// SeatedRender just snapped onto the seat anchor). Never read from CameraHandle:
				// CameraHandle sits under CameraPivot at a non-zero local offset, so look-rotation
				// swings it on an arc that punches through cab walls.
				Vector3 pos;
				if (seat != null && seat.CameraMount != null)
				{
					pos = seat.CameraMount.position;
				}
				else if (seat != null)
				{
					var off = seat.SeatedCameraOffset;
					pos = transform.position
						+ transform.right * off.x
						+ Vector3.up * off.y
						+ transform.forward * off.z;
				}
				else
				{
					pos = CameraHandle != null ? CameraHandle.position : Camera.main.transform.position;
				}
				Camera.main.transform.SetPositionAndRotation(pos, CameraPivot.rotation);
				// Skip ApplyCameraCollision while seated — the truck's chassis/cab colliders would
				// snap the camera onto the cab interior wall whenever the player looks sideways.
				// Invalidate the smoothed distance so it snaps (not eases from a stale clamp) on un-seat.
				_smoothedCameraDist = -1f;
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

			// On the input-authority client, snap the locally accumulated look to the seat's forward
			// so the seated camera starts facing where the car is pointed instead of carrying the
			// player's pre-entry aim (SeatedLateUpdate reads Input.LookRotation each frame).
			if (IsSeated && HasInputAuthority && Input != null && InCurrentSeat != null)
			{
				var seatAnchor = InCurrentSeat.Anchor;
				Input.SetLookRotation(new Vector2(0f, seatAnchor.eulerAngles.y));
			}

			if (_inventory != null)
			{
				_inventory.SuppressHeldVisual = IsSeated;
			}

			// Local input authority only: hide our own body while seated so the camera (parked at
			// eye-level inside the cab) doesn't look into our own torso. Proxies still see us in
			// the seat. _localBodyRenderers is null on non-input-authority peers.
			if (_localBodyRenderers != null)
			{
				bool visible = IsSeated == false;
				for (int i = 0; i < _localBodyRenderers.Length; i++)
				{
					var r = _localBodyRenderers[i];
					if (r != null) r.enabled = visible;
				}
			}
		}
	}
}
