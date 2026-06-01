using Fusion;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Networked projectile spawned by <see cref="ProjectileAction"/>. State authority simulates
	/// ballistic motion (velocity + drop) and uses lag-compensated raycasts for collision; clients
	/// follow the replicated transform via [Networked] NetPosition/NetRotation. On impact the SA
	/// applies damage (if a Health hitbox was hit), then despawns this projectile and spawns the
	/// item's WorldPrefab at the hit point via <see cref="PickupableItem.Stick"/> so the thrown
	/// weapon stays planted and can be picked back up.
	///
	/// Same component drives throwing knives (cosmetic spin via <see cref="VisualSpinner"/>) and
	/// arrows (no spin, oriented forward) — the visual differences live on the prefab.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	public sealed class Projectile : NetworkBehaviour
	{
		[Header("Visual")]
		[Tooltip("Optional child transform that spins about its local axis while the projectile is in flight (knives). Leave empty for arrows.")]
		[SerializeField] private Transform _visualSpinner;
		[Tooltip("Local-space rotation axis for the cosmetic spin.")]
		[SerializeField] private Vector3 _spinAxis = new Vector3(1f, 0f, 0f);
		[Tooltip("Spin rate in degrees per second. 0 disables the spin (arrows).")]
		[SerializeField] private float _spinDegreesPerSecond = 720f;

		[Header("Impact")]
		[Tooltip("Optional VFX spawned at the hit point. Local-only — no networking required.")]
		[SerializeField] private GameObject _impactPrefab;
		[Tooltip("ON: on impact the pickup becomes a dynamic Rigidbody, deflects off the surface and falls to the ground (throwing-knife pattern). OFF: the pickup stays planted exactly where it landed (arrow pattern).")]
		[SerializeField] private bool _dropOnImpact;
		[Tooltip("Fraction of the incoming velocity kept as a deflection impulse when _dropOnImpact is on. 0 = drop straight down, ~0.3 = clinks off the surface before falling.")]
		[SerializeField, Range(0f, 1f)] private float _dropDeflectFactor = 0.3f;
		[Tooltip("Seconds the dropped pickup stays non-interactable after landing, so the thrower can't instantly re-grab it.")]
		[SerializeField] private float _dropInteractionLock = 0.5f;

		[Header("Bounce")]
		[Tooltip("How many times the projectile ricochets off surfaces before it lands and sticks/drops. 0 = stick on first contact (classic arrow/knife).")]
		[SerializeField] private int _maxBounces = 2;
		[Tooltip("Fraction of speed kept on each ricochet (restitution). 0.5 halves the speed per bounce.")]
		[SerializeField, Range(0f, 1f)] private float _bounciness = 0.5f;
		[Tooltip("Minimum speed (m/s) required to deal damage on a hit (and to keep ricocheting). Keep it small — it just stops a nearly-stopped late bounce from dealing damage.")]
		[SerializeField] private float _minDamageSpeed = 2f;
		[Tooltip("After it has bounced, the projectile can hit the thrower too (ricochet self-damage). The initial throw never self-hits.")]
		[SerializeField] private bool _bouncedHitsAttacker = true;

		[Networked] public Vector3 NetPosition { get; set; }
		[Networked] public Quaternion NetRotation { get; set; }
		[Networked] public Vector3 Velocity { get; set; }
		[Networked] public float Gravity { get; set; }
		[Networked] public TickTimer Lifetime { get; set; }
		[Networked] public int Damage { get; set; }
		[Networked] public float KnockbackDistance { get; set; }
		[Networked] public int HitMaskValue { get; set; }
		[Networked] public PlayerRef Attacker { get; set; }
		[Networked] public short ItemId { get; set; }

		// SA-only ricochet counter. Reset on (re)spawn via AuthorityInitialize; not networked because
		// only the state authority ever simulates the projectile. HitOptions are built per-raycast in
		// FixedUpdateNetwork — they include PhysX always, and ignore the thrower only until the first
		// bounce (after which a ricochet can hit the thrower too).
		private int _bounceCount;

		/// <summary>
		/// SA-only. Seed the projectile's networked simulation state. Called from
		/// <see cref="ProjectileAction.Execute"/> through Runner.Spawn's onBeforeSpawned callback,
		/// so the values are present on the very first tick of FixedUpdateNetwork. The runner is
		/// passed in explicitly because <see cref="NetworkBehaviour.Runner"/> may not be wired up
		/// inside onBeforeSpawned on every Fusion build.
		/// </summary>
		public void AuthorityInitialize(NetworkRunner runner, Vector3 velocity, float gravity, int damage, float knockback,
			int hitMaskValue, PlayerRef attacker, short itemId, float lifetimeSeconds)
		{
			Velocity = velocity;
			Gravity = gravity;
			Damage = damage;
			KnockbackDistance = knockback;
			HitMaskValue = hitMaskValue;
			Attacker = attacker;
			ItemId = itemId;
			Lifetime = TickTimer.CreateFromSeconds(runner, lifetimeSeconds);
			NetPosition = transform.position;
			NetRotation = transform.rotation;
			_bounceCount = 0;
		}

		public override void FixedUpdateNetwork()
		{
			if (Object.HasStateAuthority == false) return;
			if (Lifetime.Expired(Runner))
			{
				Runner.Despawn(Object);
				return;
			}

			float dt = Runner.DeltaTime;

			// Integrate gravity into velocity first, then position. Symplectic-Euler — keeps the
			// trajectory stable at coarse tick rates better than the explicit-Euler ordering.
			Vector3 vel = Velocity;
			vel.y -= Gravity * dt;
			Vector3 fromPos = NetPosition;
			Vector3 toPos = fromPos + vel * dt;

			Vector3 delta = toPos - fromPos;
			float dist = delta.magnitude;
			if (dist > 0.0001f)
			{
				Vector3 dir = delta / dist;

				// Initial throw ignores the thrower (no instant self-hit). After a ricochet, the
				// projectile can come back and hit the thrower too — intentional bounce self-damage.
				bool ignoreAttacker = _bounceCount == 0 || _bouncedHitsAttacker == false;
				PlayerRef ignorePlayer = ignoreAttacker ? Attacker : PlayerRef.None;
				HitOptions opts = HitOptions.IncludePhysX;
				if (ignoreAttacker) opts |= HitOptions.IgnoreInputAuthority;

				if (Runner.LagCompensation.Raycast(fromPos, dir, dist, ignorePlayer, out var hit,
					    HitMaskValue, opts, QueryTriggerInteraction.Ignore))
				{
					float speed = vel.magnitude;
					Vector3 facing = speed > 0.0001f ? vel / speed : dir;
					Vector3 normal = hit.Normal != Vector3.zero ? hit.Normal : -facing;
					bool isHealthHit = hit.Hitbox != null && hit.Hitbox.Root.GetComponent<Health>() != null;

					// Surface ricochet: while bounces remain and it's still moving fast enough, reflect
					// and keep flying. A Health hit never bounces — it resolves immediately.
					if (isHealthHit == false && _bounceCount < _maxBounces && speed >= _minDamageSpeed)
					{
						_bounceCount++;
						Vector3 reflected = Vector3.Reflect(vel, normal) * _bounciness;
						NetPosition = hit.Point + normal * 0.02f; // lift off the surface so we don't immediately re-hit it
						Velocity = reflected;
						if (reflected.sqrMagnitude > 0.0001f)
							NetRotation = Quaternion.LookRotation(reflected.normalized);
						return;
					}

					// Land + resolve. Damage only when still moving fast enough — a near-stopped late
					// bounce that grazes a target deals nothing.
					NetPosition = hit.Point;
					NetRotation = Quaternion.LookRotation(facing);
					Velocity = Vector3.zero;
					ResolveImpact(hit, vel, isHealthHit && speed >= _minDamageSpeed);
					return;
				}
			}

			NetPosition = toPos;
			Velocity = vel;
			if (vel.sqrMagnitude > 0.01f)
			{
				NetRotation = Quaternion.LookRotation(vel.normalized);
			}
		}

		public override void Render()
		{
			// Proxies snap to the replicated transform (Fusion auto-interpolates [Networked] vectors
			// between ticks, so this stays smooth between FUNs). SA also drives from networked state
			// to keep its visual identical to clients' interpolated view.
			transform.SetPositionAndRotation(NetPosition, NetRotation);

			if (_visualSpinner != null && _spinDegreesPerSecond != 0f)
			{
				_visualSpinner.Rotate(_spinAxis, _spinDegreesPerSecond * Time.deltaTime, Space.Self);
			}
		}

		private void ResolveImpact(LagCompensatedHit hit, Vector3 incomingVelocity, bool applyDamage)
		{
			Vector3 facing = incomingVelocity.sqrMagnitude > 0.0001f ? incomingVelocity.normalized : transform.forward;
			Vector3 normal = hit.Normal != Vector3.zero ? hit.Normal : -facing;

			// Damage path — only when allowed (moving fast enough) and the raycast hit a Fusion Hitbox
			// with a Health component. PhysX-only hits (walls, props) skip this and just stick the pickup.
			if (applyDamage && hit.Hitbox != null)
			{
				var health = hit.Hitbox.Root.GetComponent<Health>();
				int finalDamage = Damage;
				var region = BodyHitbox.From(hit.Hitbox);
				if (region != null) finalDamage = region.Apply(Damage);
				if (health != null && health.TakeHit(finalDamage, Attacker))
				{
					if (KnockbackDistance > 0f)
					{
						var knockable = hit.Hitbox.Root.GetComponent<IKnockbackable>();
						knockable?.ApplyKnockback(NetPosition, KnockbackDistance);
					}
				}
			}

			// Spawn the pickup at the impact point so the projectile can be retrieved. Uses the item's
			// bespoke WorldPrefab if set, else the database's shared generic pickup (zero-prefab items).
			if (ItemId != 0 && ItemDatabase.Instance != null)
			{
				var def = ItemDatabase.Instance.GetById(ItemId);
				GameObject worldPrefab = def != null
					? (def.WorldPrefab != null ? def.WorldPrefab : ItemDatabase.Instance.GenericWorldPrefab)
					: null;
				if (worldPrefab != null && worldPrefab.GetComponent<NetworkObject>() != null)
				{
					Quaternion stickRot = Quaternion.LookRotation(facing);
					// When dropping, lift the spawn point off the surface along the normal so the
					// dynamic Rigidbody doesn't start interpenetrating the wall/floor it just hit.
					Vector3 spawnPos = _dropOnImpact ? NetPosition + normal * 0.05f : NetPosition;
					var spawned = Runner.Spawn(worldPrefab, spawnPos, stickRot, Attacker);
					if (spawned != null && spawned.TryGetComponent<PickupableItem>(out var pi))
					{
						pi.Initialize(ItemId, 1);
						if (_dropOnImpact)
						{
							// Reflect a fraction of the incoming velocity off the surface; gravity +
							// the Rigidbody's random tumble (set in Throw) carry it to the ground.
							Vector3 deflect = Vector3.Reflect(incomingVelocity, normal) * _dropDeflectFactor;
							pi.Throw(deflect, _dropInteractionLock);
						}
						else
						{
							pi.Stick(spawnPos, stickRot);
						}
					}
				}
			}

			if (_impactPrefab != null)
			{
				Instantiate(_impactPrefab, NetPosition, Quaternion.LookRotation(normal));
			}

			Runner.Despawn(Object);
		}
	}
}
