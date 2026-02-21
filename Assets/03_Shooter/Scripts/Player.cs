using UnityEngine;
using Fusion;
using Fusion.Addons.SimpleKCC;
using UnityEngine.Rendering;

namespace Starter.Shooter
{
	/// <summary>
	/// Main player scrip - controls player movement and animations.
	/// </summary>
	public sealed class Player : NetworkBehaviour
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
		public GameObject[] FirstPersonOverlayObjects;

		[Header("Movement Setup")]
		public float WalkSpeed = 2f;
		public float JumpImpulse = 10f;
		public float UpGravity = 25f;
		public float DownGravity = 40f;

		[Header("Movement Accelerations")]
		public float GroundAcceleration = 55f;
		public float GroundDeceleration = 25f;
		public float AirAcceleration = 25f;
		public float AirDeceleration = 1.3f;

		[Header("Fire Setup")]
		public LayerMask HitMask;
		public GameObject ImpactPrefab;
		public ParticleSystem MuzzleParticle;

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

		[Networked]
		private Vector3 _moveVelocity { get; set; }
		[Networked, OnChangedRender(nameof(OnJumpingChanged))]
		private NetworkBool _isJumping { get; set; }
		[Networked]
		private Vector3 _hitPosition { get; set; }
		[Networked]
		private Vector3 _hitNormal { get; set; }
		[Networked]
		private int _fireCount { get; set; }

		// Animation IDs
		private int _animIDSpeedX;
		private int _animIDSpeedZ;
		private int _animIDMoveSpeedZ;
		private int _animIDGrounded;
		private int _animIDPitch;
		private int _animIDShoot;

		private int _visibleFireCount;

		public void Respawn(Vector3 position)
		{
			ChickenKills = 0;
			Health.Revive();

			KCC.SetActive(true);
			KCC.SetPosition(position);
			KCC.SetLookRotation(0f, 0f);

			_moveVelocity = Vector3.zero;
		}

		public override void Spawned()
		{
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

				// Some objects (e.g. weapon) are renderer with secondary Overlay camera.
				// This prevents weapon clipping into the wall when close to the wall.
				int overlayLayer = LayerMask.NameToLayer("FirstPersonOverlay");
				for (int i = 0; i < FirstPersonOverlayObjects.Length; i++)
				{
					FirstPersonOverlayObjects[i].layer = overlayLayer;
				}

				// Look rotation interpolation is skipped for local player.
				// Look rotation is set manually in Render.
				KCC.Settings.ForcePredictedLookRotation = true;
			}
		}

		public override void FixedUpdateNetwork()
		{
			if (Health.IsAlive && GetInput<GameplayInput>(out var input))
			{
				ProcessInput(input, Input.PreviousButtons);
			}
			else
			{
				// Continue with KCC movement (e.g. fall) even
				// when player is dead or input is missing.
				MovePlayer(Vector3.zero, 0f);
			}

			if (KCC.IsGrounded)
			{
				// Stop jumping
				_isJumping = false;
			}

			// Disable collisions and hits when player is dead
			HitboxRoot.HitboxRootActive = Health.IsAlive;
			KCC.SetActive(Health.IsAlive);
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
		}

		private void Awake()
		{
			AssignAnimationIDs();
		}

		private void LateUpdate()
		{
			if (Health.IsAlive == false)
				return;

			// Update camera pivot (influences ChestIK)
			// (KCC look rotation is set earlier in Render)
			var pitchRotation = KCC.GetLookRotation(true, false);
			CameraPivot.localRotation = Quaternion.Euler(pitchRotation);

			// Dummy IK solution, we are snapping chest bone to prepared ChestTargetPosition position
			// Lerping blends the fixed position with little bit of animation position.
			float blendAmount = HasInputAuthority ? 0.05f : 0.2f;
			ChestBone.position = Vector3.Lerp(ChestTargetPosition.position, ChestBone.position, blendAmount);
			ChestBone.rotation = Quaternion.Lerp(ChestTargetPosition.rotation, ChestBone.rotation, blendAmount);

			// Only InputAuthority needs to update camera
			if (HasInputAuthority)
			{
				// Transfer properties from camera handle to Main Camera.
				Camera.main.transform.SetPositionAndRotation(CameraHandle.position, CameraHandle.rotation);
			}
		}

		private void ProcessInput(GameplayInput input, NetworkButtons previousButtons)
		{
			KCC.SetLookRotation(input.LookRotation, -90f, 90f);

			// Calculate correct move direction from input (rotated based on latest KCC rotation)
			var moveDirection = KCC.TransformRotation * new Vector3(input.MoveDirection.x, 0f, input.MoveDirection.y);
			var desiredMoveVelocity = moveDirection * WalkSpeed;

			float jumpImpulse = 0f;

			// Comparing current input buttons to previous input buttons - this prevents glitches when input is lost
			if (KCC.IsGrounded && input.Buttons.WasPressed(previousButtons, EInputButton.Jump))
			{
				// Set world space jump vector
				jumpImpulse = JumpImpulse;
				_isJumping = true;
			}

			MovePlayer(desiredMoveVelocity, jumpImpulse);

			// Update camera pivot so fire transform (CameraHandle) is correct
			var pitchRotation = KCC.GetLookRotation(true, false);
			CameraPivot.localRotation = Quaternion.Euler(pitchRotation);

			if (input.Buttons.WasPressed(previousButtons, EInputButton.Fire))
			{
				Fire();
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

			KCC.Move(_moveVelocity, jumpImpulse);
		}

		private void Fire()
		{
			// Clear hit position in case nothing will be hit
			_hitPosition = Vector3.zero;

			var hitOptions = HitOptions.IncludePhysX | HitOptions.IgnoreInputAuthority;

			// Whole projectile path and effects are immediately processed (= hitscan projectile)
			if (Runner.LagCompensation.Raycast(CameraHandle.position, CameraHandle.forward, 200f,
				    Object.InputAuthority, out var hit, HitMask, hitOptions, QueryTriggerInteraction.Ignore) == true)
			{
				// Deal damage
				var health = hit.Hitbox != null ? hit.Hitbox.Root.GetComponent<Health>() : null;
				if (health != null && health.TakeHit(1))
				{
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
				FireSound.PlayOneShot(FireSound.clip);
				MuzzleParticle.Play();
				Animator.SetTrigger(_animIDShoot);

				if (_hitPosition != Vector3.zero)
				{
					// Impact gets destroyed automatically with DestroyAfter script
					Instantiate(ImpactPrefab, _hitPosition, Quaternion.LookRotation(_hitNormal));
				}
			}

			_visibleFireCount = _fireCount;
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

		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
		private void RPC_SetNickname(string nickname)
		{
			Nickname = nickname;
		}
	}
}
