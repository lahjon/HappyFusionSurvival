using Fusion;
using Fusion.Addons.SimpleKCC;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Shared host-authoritative physics for dynamic world objects: the <see cref="IBuoyantBody"/>
	/// contract a <see cref="WaterVolume"/> reads, combat <see cref="IKnockbackable"/> knockback, and the
	/// manual player walk-into push (SimpleKCC is kinematic, so PhysX never imparts contact velocity —
	/// we sweep for overlapping players and shove the body ourselves).
	///
	/// Subclassed by <see cref="PhysicsProp"/> (loose props — barrels, cans, the rubber duck) and
	/// <see cref="Starter.Common.Inventory.PickupableItem"/> (dropped item stacks). Both react to forces
	/// identically; they differ only in their extra behaviour (pickup/interaction) and how their pose
	/// replicates (NetworkTransform vs a networked NetPosition). Only the state authority simulates —
	/// proxies run kinematic rigidbodies, which every method here skips via the <c>isKinematic</c> guard.
	///
	/// All forces are <see cref="ForceMode.Impulse"/> so a body's Mass stays meaningful across every
	/// source: a heavy prop shrugs off a pistol shot or a shove while a light one is sent flying.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	[RequireComponent(typeof(Rigidbody))]
	public abstract class PhysicsBody : NetworkBehaviour, IKnockbackable, IBuoyantBody
	{
		/// <summary>How the body behaves on the host the moment it spawns.</summary>
		public enum StartPhysicsMode
		{
			/// <summary>Simulates immediately — reacts to gravity and forces from frame one (default).</summary>
			Normal,
			/// <summary>Host starts the body kinematic so it neither simulates nor replicates (zero cost),
			/// staying put until the first disturbance — a combat hit or a player walking into it — wakes it
			/// into a Normal dynamic body. Once woken it behaves exactly like Normal (it won't re-freeze; it
			/// just sleeps when it settles, like any other prop).</summary>
			KinematicUntilHit,
		}

		[Header("Spawn")]
		[Tooltip("Normal: simulates from spawn (reacts to gravity/forces immediately). " +
		         "KinematicUntilHit: spawns frozen (no simulation or network traffic) until something hits it — " +
		         "a shot/melee/explosion or a player walking into it — then it becomes a normal dynamic body.")]
		[SerializeField] protected StartPhysicsMode _startMode = StartPhysicsMode.Normal;

		[Header("Water")]
		[Tooltip("ON: this body floats to the surface of any WaterVolume it enters and self-rights there. " +
		         "OFF (default): it sinks. PickupableItem overrides this to read the item's ItemDefinition.")]
		[SerializeField] protected bool _floats = false;

		[Header("Hit Response (shoot / melee / explosion)")]
		[Tooltip("Multiplier converting a CombatAction's KnockbackDistance (meters) into a horizontal impulse. " +
		         "Because it's an impulse, a heavier Mass produces less velocity — tune against your prop masses.")]
		[SerializeField] protected float _knockbackImpulseScale = 6f;
		[Tooltip("Upward impulse added per unit of KnockbackDistance, for a small toss/scatter arc.")]
		[SerializeField] protected float _knockbackUpScale = 3f;

		[Header("Player Push (walk into it — intentionally weaker than a hit)")]
		[Tooltip("Extra radius (m) beyond the collider bounds used to detect player overlap. SimpleKCC is " +
		         "kinematic, so this manual sweep is what makes the body move when you walk into it.")]
		[SerializeField] protected float _playerPushRadius = 0.2f;
		[Tooltip("Baseline impulse when a stationary player just leans on the body.")]
		[SerializeField] protected float _baselinePushImpulse = 0.6f;
		[Tooltip("Impulse gained per (m/s) of the player's speed moving INTO the body. Lets a running shove move it more.")]
		[SerializeField] protected float _pushSpeedScale = 0.8f;
		[Tooltip("Upper bound on the walk-into impulse so a sprinting player can't punt it across the map.")]
		[SerializeField] protected float _maxPushImpulse = 5f;

		[Header("Center of mass (only used when this body Floats)")]
		[Tooltip("ON: override the Rigidbody's center of mass with the offset below. A low COM (negative Y) " +
		         "makes the body bottom-heavy so it self-rights and floats upright instead of tumbling. " +
		         "Only honored when Floats is on — a sinker/land prop keeps Unity's auto-computed COM. " +
		         "PickupableItem sources this from the ItemDefinition.")]
		[SerializeField] protected bool _overrideCenterOfMass = false;
		[Tooltip("Local-space center of mass when the override is on.")]
		[SerializeField] protected Vector3 _centerOfMass = Vector3.zero;

		[Header("Settling (auto-sleep so we stop simulating + replicating)")]
		[Tooltip("Below this linear speed (m/s) the body counts as at rest. Once it's been at rest for the " +
		         "delay below it's put to sleep, so the host stops moving it and the network stops replicating it.")]
		[SerializeField] protected float _sleepLinearThreshold = 0.12f;
		[Tooltip("Below this angular speed (rad/s) the body counts as at rest. Needed because a round prop " +
		         "(e.g. the duck's sphere collider) can roll indefinitely without its linear speed alone settling.")]
		[SerializeField] protected float _sleepAngularThreshold = 0.5f;
		[Tooltip("Seconds the body must stay below both thresholds before it's forced to sleep.")]
		[SerializeField] protected float _sleepDelay = 1f;

		protected Rigidbody _rb;
		protected Collider _col;
		private float _restTimer;
		private static readonly Collider[] s_pushBuffer = new Collider[8];

		/// <summary>Whether this body floats in water. Virtual so a subclass can source it elsewhere
		/// (PickupableItem reads the item's ItemDefinition, since the generic pickup prefab is shared).</summary>
		public virtual bool Floats => _floats;

		/// <summary>
		/// Freeze the body before any physics runs. A scene-placed Rigidbody starts simulating in PhysX the
		/// instant the scene loads — which is BEFORE Fusion calls <see cref="Spawned"/>, where authority and
		/// start mode are decided. Without this, a body authored non-kinematic falls/tips during that gap (a
		/// flamingo on thin legs topples in a couple of ticks) and then freezes in the wrong pose. Awake runs
		/// before the first physics step, so we kinematic-freeze every body here; <see cref="Spawned"/> then
		/// sets the real state — dynamic for a Normal host body, kinematic for a proxy or KinematicUntilHit.
		/// </summary>
		protected virtual void Awake()
		{
			_rb = GetComponent<Rigidbody>();
			if (_rb != null) _rb.isKinematic = true;
		}

		/// <summary>Cache the Rigidbody/Collider and apply the authored center of mass.
		/// Call from the subclass's <c>Spawned()</c>.</summary>
		protected void CachePhysicsRefs()
		{
			_rb = GetComponent<Rigidbody>();
			_col = GetComponentInChildren<Collider>();
			ApplyCenterOfMass();
		}

		/// <summary>The center-of-mass override for this body. Virtual so PickupableItem can source it
		/// from the ItemDefinition rather than the shared prefab. Returns false to use Unity's default.</summary>
		protected virtual bool TryGetCenterOfMass(out Vector3 com)
		{
			com = _centerOfMass;
			return _overrideCenterOfMass;
		}

		/// <summary>Push the authored center of mass onto the Rigidbody (or reset to Unity's default).
		/// Re-callable: PickupableItem applies it again once its item id resolves. COM only matters for
		/// self-righting a floating body, so it's only honored when <see cref="Floats"/> is true —
		/// a sinker or land prop keeps Unity's auto-computed COM.</summary>
		protected void ApplyCenterOfMass()
		{
			if (_rb == null) return;
			if (Floats && TryGetCenterOfMass(out var com)) _rb.centerOfMass = com;
			else _rb.ResetCenterOfMass();
		}

		/// <summary>
		/// Host-only. Once the body has stayed below the rest thresholds for <c>_sleepDelay</c>, force it to
		/// <see cref="Rigidbody.Sleep"/>. A sleeping body neither integrates (so it can't keep drifting/rolling
		/// on land or bobbing forever at the surface) nor produces transform deltas (so the NetworkTransform /
		/// networked position stops replicating). Any force — knockback, walk-into push, buoyancy, or a
		/// collision — wakes it again, and water buoyancy resumes from <see cref="WaterVolume"/>.
		/// </summary>
		protected void TickSettle(float dt)
		{
			if (_rb == null || _rb.isKinematic || _rb.IsSleeping()) return;

			if (_rb.linearVelocity.magnitude < _sleepLinearThreshold &&
			    _rb.angularVelocity.magnitude < _sleepAngularThreshold)
			{
				_restTimer += dt;
				if (_restTimer >= _sleepDelay) _rb.Sleep();
			}
			else
			{
				_restTimer = 0f;
			}
		}

		/// <summary>Host-only. The single entry point every force path calls before imparting an impulse.
		/// If this is a <see cref="StartPhysicsMode.KinematicUntilHit"/> body that's still frozen, flip it to a
		/// simulating dynamic body so the impulse (and gravity from here on) takes effect; otherwise just wake
		/// it from sleep. Proxies follow the NetworkTransform regardless, so there's nothing to replicate here.</summary>
		protected void WakeOnImpact()
		{
			if (_rb == null) return;
			if (_rb.isKinematic)
			{
				// First disturbance for a KinematicUntilHit body — become a normal dynamic prop from now on.
				_rb.isKinematic = false;
				_rb.interpolation = RigidbodyInterpolation.Interpolate;
				_restTimer = 0f;
			}
			else if (_rb.IsSleeping())
			{
				_rb.WakeUp();
			}
		}

		// --- IBuoyantBody: WaterVolume reads these to apply buoyancy/self-righting on the host ---

		Rigidbody IBuoyantBody.Body => _rb;
		bool IBuoyantBody.Floats => Floats;

		// --- IKnockbackable: shooting, melee and explosions all route here ---

		// fromPosition is the push origin (attacker position for melee/hitscan, blast centre for a grenade);
		// distance is the action's KnockbackDistance, already scaled by charge / explosion falloff.
		void IKnockbackable.ApplyKnockback(Vector3 fromPosition, float distance)
		{
			if (Object.HasStateAuthority == false) return;
			if (_rb == null || distance <= 0f) return;

			Vector3 dir = transform.position - fromPosition;
			dir.y = 0f;
			if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;
			dir.Normalize();

			Vector3 impulse = dir * (distance * _knockbackImpulseScale);
			impulse.y += distance * _knockbackUpScale;

			WakeOnImpact();
			_rb.AddForce(impulse, ForceMode.Impulse);
		}

		// --- Walk-into push (SimpleKCC is kinematic, so do it manually — weaker than a hit) ---

		// SimpleKCC is a kinematic capsule, so PhysX never imparts contact velocity to the dynamic
		// rigidbody. Each tick on the host, check for player overlap and shove the body away horizontally.
		protected void ApplyPlayerPush()
		{
			if (_rb == null || _col == null) return;
			// A kinematic body normally skips the sweep, but a KinematicUntilHit prop must keep sweeping while
			// frozen so that a player walking into it counts as a "hit" and wakes it (WakeOnImpact, below).
			if (_rb.isKinematic && _startMode != StartPhysicsMode.KinematicUntilHit) return;

			Vector3 center = _rb.position;
			var ext = _col.bounds.extents;
			float radius = Mathf.Max(ext.x, ext.y, ext.z) + _playerPushRadius;

			int count = Physics.OverlapSphereNonAlloc(center, radius, s_pushBuffer, ~0, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < count; i++)
			{
				var col = s_pushBuffer[i];
				if (col == null) continue;
				if (col.attachedRigidbody == _rb) continue;

				var kcc = col.GetComponentInParent<SimpleKCC>();
				if (kcc == null) continue;

				Vector3 away = center - kcc.transform.position;
				away.y = 0f;
				if (away.sqrMagnitude < 1e-6f)
				{
					// Player standing directly on the body — pick an arbitrary horizontal direction.
					away = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
					if (away.sqrMagnitude < 1e-6f) away = Vector3.right;
				}
				away.Normalize();

				Vector3 playerVel = kcc.RealVelocity; playerVel.y = 0f;
				float intoSpeed = Mathf.Max(Vector3.Dot(playerVel, away), 0f);
				float impulse = Mathf.Min(_baselinePushImpulse + intoSpeed * _pushSpeedScale, _maxPushImpulse);

				WakeOnImpact();
				_rb.AddForce(away * impulse, ForceMode.Impulse);
			}
		}
	}
}
