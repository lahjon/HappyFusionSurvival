using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Driveable pickup. State authority lives on the host; input authority is transferred
	/// to the driver player by <see cref="Seat"/> on entry, so <see cref="GetInput{GameplayInput}"/>
	/// in <see cref="FixedUpdateNetwork"/> returns the driver's input directly.
	///
	/// No Fusion physics addon in this project — position/rotation are replicated by a
	/// <see cref="NetworkTransform"/>, and only the host's Rigidbody is dynamic (clients are
	/// kinematic so the NetworkTransform's writes aren't fought by physics). World collisions
	/// only resolve on the host; clients see the interpolated result.
	///
	/// Arcade-style: tank-like turning. Lateral velocity is killed each tick so the truck
	/// doesn't drift sideways; throttle / brake along forward axis; steering yaw scales down
	/// with speed for a realistic feel.
	/// </summary>
	[RequireComponent(typeof(Rigidbody))]
	[RequireComponent(typeof(NetworkTransform))]
	public sealed class Vehicle : NetworkBehaviour
	{
		[Header("Seats")]
		[Tooltip("Optional explicit driver seat. If null, the first registered Seat with Role=Driver is used.")]
		public Seat DriverSeatOverride;

		[Header("Tuning — Throttle")]
		[Min(0f)] public float MaxForwardSpeed = 14f;
		[Min(0f)] public float MaxReverseSpeed = 6f;
		[Min(0f)] public float Acceleration = 18f;
		[Min(0f)] public float BrakeDeceleration = 22f;
		[Tooltip("Drag-style decay applied to forward speed when no throttle input. m/s²")]
		[Min(0f)] public float IdleDeceleration = 6f;

		[Header("Tuning — Steering")]
		[Tooltip("Yaw rate (deg/s) at zero speed.")]
		[Min(0f)] public float MaxTurnDegPerSec = 90f;
		[Tooltip("Yaw rate (deg/s) at full forward speed.")]
		[Min(0f)] public float MinTurnDegPerSec = 28f;

		[Header("Tuning — Brake / Drift")]
		[Tooltip("Forward speed threshold (m/s). Brake held below this just stops the truck; above it preserves lateral velocity so the truck can slide.")]
		[Min(0f)] public float DriftMinSpeed = 5f;
		[Tooltip("Rate (m/s²) at which sideways momentum bleeds off during a drift. Lower = longer slides.")]
		[Min(0f)] public float DriftLateralDecay = 4f;

		[Header("Tuning — Physics")]
		[Tooltip("Local-space center-of-mass offset, applied on Spawned. Lower than 0 keeps the truck from flipping.")]
		public Vector3 CenterOfMassOffset = new Vector3(0f, -0.4f, 0f);

		[Header("Wheels (cosmetic)")]
		[Tooltip("Wheel transforms — local rotation around their forward axis is spun proportional to forward speed.")]
		public Transform[] WheelVisuals;
		[Tooltip("Radius (m) used to convert forward speed into wheel-spin rotation. Also the world radius of the auto-created wheel sphere colliders.")]
		public float WheelRadius = 0.35f;

		[Header("Wheel Physics")]
		[Tooltip("Auto-add a zero-friction SphereCollider to each WheelVisual on Spawned so the truck rests on its wheels (chassis stays clear of the ground). Disable if you author wheel colliders manually.")]
		public bool AutoSetupWheelColliders = true;

		[Header("Honk")]
		[Tooltip("Optional one-shot AudioSource. Played on every peer when the driver presses Fire.")]
		public AudioSource HonkSource;

		[Networked, OnChangedRender(nameof(OnHonkChanged))]
		private int _honkCount { get; set; }

		[Networked] private NetworkButtons _previousButtons { get; set; }

		private int _visibleHonkCount;
		private Rigidbody _rb;
		private readonly List<Seat> _seats = new List<Seat>(4);

		public Seat DriverSeat
		{
			get
			{
				if (DriverSeatOverride != null) return DriverSeatOverride;
				for (int i = 0; i < _seats.Count; i++)
				{
					if (_seats[i] != null && _seats[i].Role == ESeatRole.Driver) return _seats[i];
				}
				return null;
			}
		}

		public override void Spawned()
		{
			_rb = GetComponent<Rigidbody>();
			_rb.centerOfMass = CenterOfMassOffset;
			// Only the host runs physics. Proxies are kinematic so NetworkTransform
			// writes aren't fought by the local PhysX integration.
			_rb.isKinematic = HasStateAuthority == false;
			if (HonkSource == null) HonkSource = GetComponent<AudioSource>();
			_visibleHonkCount = _honkCount;

			if (AutoSetupWheelColliders) EnsureWheelColliders();
		}

		// Cached shared zero-friction material. Sphere colliders on the wheels need this so they
		// don't drag the velocity we write each FixedUpdateNetwork tick — default PhysX friction
		// at four ground contacts is enough to make ApplyDrive ineffective.
		private static PhysicsMaterial _sharedWheelMaterial;

		private void EnsureWheelColliders()
		{
			if (WheelVisuals == null) return;

			if (_sharedWheelMaterial == null)
			{
				_sharedWheelMaterial = new PhysicsMaterial("Vehicle_WheelZeroFriction")
				{
					staticFriction = 0f,
					dynamicFriction = 0f,
					bounciness = 0f,
					frictionCombine = PhysicsMaterialCombine.Minimum,
					bounceCombine = PhysicsMaterialCombine.Minimum,
				};
			}

			for (int i = 0; i < WheelVisuals.Length; i++)
			{
				var w = WheelVisuals[i];
				if (w == null) continue;

				var col = w.GetComponent<SphereCollider>();
				if (col == null) col = w.gameObject.AddComponent<SphereCollider>();

				// PhysX scales sphere collider radius by max(lossyScale). Compensate so the world-space
				// radius equals WheelRadius regardless of the wheel transform's non-uniform scale.
				Vector3 ls = w.lossyScale;
				float maxScale = Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
				col.center = Vector3.zero;
				col.radius = maxScale > 0.0001f ? WheelRadius / maxScale : WheelRadius;
				col.isTrigger = false;
				col.sharedMaterial = _sharedWheelMaterial;
			}
		}

		public void RegisterSeat(Seat seat)
		{
			if (seat == null || _seats.Contains(seat)) return;
			_seats.Add(seat);
		}

		public void UnregisterSeat(Seat seat)
		{
			_seats.Remove(seat);
		}

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false) return;

			var driver = DriverSeat;
			bool hasDriver = driver != null && driver.Occupant != PlayerRef.None;

			if (hasDriver && GetInput<GameplayInput>(out var input))
			{
				ApplyDrive(input);
				ApplyHonk(input);
				_previousButtons = input.Buttons;
			}
			else
			{
				ApplyIdleDecay();
				_previousButtons = default;
			}
		}

		public override void Render()
		{
			SpinWheelsVisual();
		}

		private void ApplyDrive(GameplayInput input)
		{
			float throttle = Mathf.Clamp(input.MoveDirection.y, -1f, 1f);
			float steer = Mathf.Clamp(input.MoveDirection.x, -1f, 1f);
			bool brakeHeld = input.Buttons.IsSet(EInputButton.Brake);
			float dt = Runner.DeltaTime;

			Vector3 fwd = transform.forward;
			Vector3 right = transform.right;
			Vector3 vel = _rb.linearVelocity;
			float currentForward = Vector3.Dot(vel, fwd);
			float currentLateral = Vector3.Dot(vel, right);

			if (brakeHeld)
			{
				// Brake button overrides throttle — drag forward speed toward 0 at the brake rate.
				currentForward = Mathf.MoveTowards(currentForward, 0f, BrakeDeceleration * dt);
			}
			else if (Mathf.Abs(throttle) > 0.05f)
			{
				float maxSpeed = throttle >= 0f ? MaxForwardSpeed : MaxReverseSpeed;
				float target = throttle * maxSpeed;
				// Use a stronger response when the player is reversing the existing direction (treat as brake).
				bool braking = (currentForward > 0f && throttle < 0f) || (currentForward < 0f && throttle > 0f);
				float rate = braking ? BrakeDeceleration + Acceleration : Acceleration;
				currentForward = Mathf.MoveTowards(currentForward, target, rate * dt);
			}
			else
			{
				currentForward = Mathf.MoveTowards(currentForward, 0f, IdleDeceleration * dt);
			}

			// Drift: while holding brake at speed, preserve lateral velocity so steering rotates the
			// truck's nose without erasing its momentum — the body slides. Otherwise hard-kill lateral
			// velocity for the tank-style no-drift baseline.
			bool sliding = brakeHeld && Mathf.Abs(currentForward) > DriftMinSpeed;
			if (sliding)
			{
				currentLateral = Mathf.MoveTowards(currentLateral, 0f, DriftLateralDecay * dt);
			}
			else
			{
				currentLateral = 0f;
			}

			Vector3 newVel = fwd * currentForward + right * currentLateral;
			newVel.y = vel.y; // preserve gravity / vertical
			_rb.linearVelocity = newVel;

			// Speed-dependent yaw rate. Reverse steering uses the same direction as forward
			// (push left, nose-left) for an arcade feel — flipping sign here would feel
			// like a real car backing up, which is confusing in a tank-style controller.
			float absForward = Mathf.Abs(currentForward);
			float speedT = MaxForwardSpeed > 0f ? Mathf.InverseLerp(0f, MaxForwardSpeed, absForward) : 0f;
			float turnRate = Mathf.Lerp(MaxTurnDegPerSec, MinTurnDegPerSec, speedT);
			float yawDelta = steer * turnRate * dt;
			if (Mathf.Abs(yawDelta) > 0.0001f)
			{
				_rb.MoveRotation(_rb.rotation * Quaternion.Euler(0f, yawDelta, 0f));
			}

			// Damp angular drift so a roll from a collision settles instead of spinning forever.
			_rb.angularVelocity = Vector3.MoveTowards(_rb.angularVelocity, Vector3.zero, 3f * dt);
		}

		private void ApplyIdleDecay()
		{
			float dt = Runner.DeltaTime;
			Vector3 fwd = transform.forward;
			Vector3 vel = _rb.linearVelocity;
			float currentForward = Vector3.Dot(vel, fwd);
			currentForward = Mathf.MoveTowards(currentForward, 0f, IdleDeceleration * dt);
			Vector3 newVel = fwd * currentForward;
			newVel.y = vel.y;
			_rb.linearVelocity = newVel;
			_rb.angularVelocity = Vector3.MoveTowards(_rb.angularVelocity, Vector3.zero, 3f * dt);
		}

		private void ApplyHonk(GameplayInput input)
		{
			if (input.Buttons.WasPressed(_previousButtons, EInputButton.Fire))
			{
				_honkCount++;
			}
		}

		private void OnHonkChanged()
		{
			if (_honkCount == _visibleHonkCount) return;
			_visibleHonkCount = _honkCount;
			if (HonkSource != null) HonkSource.Play();
		}

		private void SpinWheelsVisual()
		{
			if (WheelVisuals == null || WheelVisuals.Length == 0 || WheelRadius <= 0f) return;
			if (_rb == null) return;

			float forwardSpeed = Vector3.Dot(_rb.linearVelocity, transform.forward);
			float degPerSecond = (forwardSpeed / (2f * Mathf.PI * WheelRadius)) * 360f;
			float delta = degPerSecond * Time.deltaTime;

			// Rotate around the truck's right axis (world space) so the wheel mesh orientation doesn't matter.
			Vector3 axis = transform.right;
			for (int i = 0; i < WheelVisuals.Length; i++)
			{
				var w = WheelVisuals[i];
				if (w == null) continue;
				w.Rotate(axis, delta, Space.World);
			}
		}
	}
}
