using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Networked thrown grenade spawned by <see cref="GrenadeAction"/>. Unlike <see cref="Projectile"/>
	/// it deals NO damage on contact — it bounces off surfaces (state-authority PhysX raycast) and
	/// arms a fuse. When the fuse expires it detonates: an <see cref="Physics.OverlapSphereNonAlloc"/>
	/// at the blast centre damages every <see cref="Health"/> in range with linear distance falloff
	/// (100% at the centre → <see cref="ExplosionDamage"/> * <see cref="MinDamageFraction"/> at the rim).
	///
	/// State authority simulates the arc + detonation; clients follow the replicated transform via the
	/// [Networked] NetPosition/NetRotation. The blast VFX is driven off a single [Networked] flag with an
	/// OnChangedRender hook so every peer plays it once (tolerates a lost tick better than an RPC-to-all);
	/// the husk lingers <see cref="_lingerAfterDetonation"/> seconds after detonating so that flag reliably
	/// reaches clients before the object despawns.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	public sealed class GrenadeProjectile : NetworkBehaviour
	{
		[Header("Visual")]
		[Tooltip("Mesh child hidden the moment the grenade detonates (so the blast VFX replaces it). Optional.")]
		[SerializeField] private GameObject _visual;
		[Tooltip("Optional child transform that spins about its local axis while the grenade is airborne.")]
		[SerializeField] private Transform _visualSpinner;
		[Tooltip("Local-space spin axis for the cosmetic tumble.")]
		[SerializeField] private Vector3 _spinAxis = new Vector3(1f, 0f, 0f);
		[Tooltip("Tumble rate in degrees/second. 0 disables the spin.")]
		[SerializeField] private float _spinDegreesPerSecond = 360f;

		[Header("Bounce")]
		[Tooltip("Fraction of speed kept on each ricochet off a surface (restitution). 0 = sticks where it lands, 0.4 = lively roll.")]
		[SerializeField, Range(0f, 1f)] private float _bounciness = 0.4f;
		[Tooltip("Below this speed (m/s) after a bounce the grenade stops integrating and simply rests until the fuse blows.")]
		[SerializeField] private float _restSpeed = 0.6f;

		[Header("Explosion")]
		[Tooltip("Local-only VFX spawned at the blast centre on every peer when the grenade detonates. Optional.")]
		[SerializeField] private GameObject _explosionPrefab;
		[Tooltip("Seconds the husk lingers after detonating so the networked Detonated flag reaches clients to play the VFX, then it despawns.")]
		[SerializeField] private float _lingerAfterDetonation = 0.25f;

		[Networked] public Vector3 NetPosition { get; set; }
		[Networked] public Quaternion NetRotation { get; set; }
		[Networked] public Vector3 Velocity { get; set; }
		[Networked] public float Gravity { get; set; }
		[Networked] public TickTimer Fuse { get; set; }
		[Networked] public int ExplosionDamage { get; set; }
		[Networked] public float ExplosionRadius { get; set; }
		[Networked] public float MinDamageFraction { get; set; }
		[Networked] public float KnockbackDistance { get; set; }
		[Networked] public int HitMaskValue { get; set; }
		[Networked] public PlayerRef Attacker { get; set; }
		[Networked, OnChangedRender(nameof(OnDetonatedChanged))] public NetworkBool Detonated { get; set; }

		// SA-only — set when the fuse blows so the husk lingers a beat before despawning.
		private TickTimer _despawnTimer;

		// Reused across detonations on the single-threaded simulation. _damaged dedupes the multiple
		// colliders a single body can contribute to the overlap so each Health takes one hit.
		private static readonly Collider[] _overlap = new Collider[32];
		private static readonly List<Health> _damaged = new List<Health>(16);

		/// <summary>
		/// SA-only. Seed the grenade's networked state. Called from <see cref="GrenadeAction.Execute"/>
		/// through Runner.Spawn's onBeforeSpawned callback so the values are present on the first tick of
		/// FixedUpdateNetwork. The runner is passed explicitly because <see cref="NetworkBehaviour.Runner"/>
		/// may not be wired up inside onBeforeSpawned on every Fusion build.
		/// </summary>
		public void AuthorityInitialize(NetworkRunner runner, Vector3 velocity, float gravity, float fuseSeconds,
			int explosionDamage, float explosionRadius, float minDamageFraction, float knockback, int hitMaskValue,
			PlayerRef attacker)
		{
			Velocity = velocity;
			Gravity = gravity;
			Fuse = TickTimer.CreateFromSeconds(runner, fuseSeconds);
			ExplosionDamage = explosionDamage;
			ExplosionRadius = explosionRadius;
			MinDamageFraction = minDamageFraction;
			KnockbackDistance = knockback;
			HitMaskValue = hitMaskValue;
			Attacker = attacker;
			Detonated = false;
			NetPosition = transform.position;
			NetRotation = transform.rotation;
		}

		public override void FixedUpdateNetwork()
		{
			if (Object.HasStateAuthority == false) return;

			if (Detonated)
			{
				if (_despawnTimer.Expired(Runner)) Runner.Despawn(Object);
				return;
			}

			if (Fuse.Expired(Runner))
			{
				Detonate();
				return;
			}

			float dt = Runner.DeltaTime;

			// Symplectic-Euler: fold gravity into velocity, then advance position — stable at coarse ticks.
			Vector3 vel = Velocity;
			vel.y -= Gravity * dt;
			Vector3 fromPos = NetPosition;
			Vector3 toPos = fromPos + vel * dt;

			Vector3 delta = toPos - fromPos;
			float dist = delta.magnitude;
			if (dist > 0.0001f)
			{
				Vector3 dir = delta / dist;
				// Bounce off the world (Default layer) — never deals contact damage. Plain PhysX on the host
				// is enough: the grenade ricochets off static geometry and bodies alike, no lag-comp needed.
				if (Physics.Raycast(fromPos, dir, out var hit, dist, HitMaskValue, QueryTriggerInteraction.Ignore))
				{
					Vector3 normal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal : -dir;
					Vector3 reflected = Vector3.Reflect(vel, normal) * _bounciness;
					NetPosition = hit.point + normal * 0.03f; // lift off the surface so we don't re-hit it next tick

					if (reflected.sqrMagnitude <= _restSpeed * _restSpeed)
					{
						// Too slow to keep rolling — settle and wait out the fuse in place.
						Velocity = Vector3.zero;
					}
					else
					{
						Velocity = reflected;
						NetRotation = Quaternion.LookRotation(reflected.normalized);
					}
					return;
				}
			}

			NetPosition = toPos;
			Velocity = vel;
			if (vel.sqrMagnitude > 0.01f)
				NetRotation = Quaternion.LookRotation(vel.normalized);
		}

		public override void Render()
		{
			// Proxies snap to the replicated transform (Fusion interpolates [Networked] vectors between ticks).
			transform.SetPositionAndRotation(NetPosition, NetRotation);

			if (Detonated == false && _visualSpinner != null && _spinDegreesPerSecond != 0f)
				_visualSpinner.Rotate(_spinAxis, _spinDegreesPerSecond * Time.deltaTime, Space.Self);
		}

		/// <summary>
		/// SA-only. Radius damage with linear distance falloff, applied once per <see cref="Health"/> in range.
		/// Flags <see cref="Detonated"/> so every peer plays the blast VFX, then schedules a short-lived despawn.
		/// </summary>
		private void Detonate()
		{
			Velocity = Vector3.zero;
			Detonated = true;
			_despawnTimer = TickTimer.CreateFromSeconds(Runner, _lingerAfterDetonation);

			Vector3 center = NetPosition;
			float radius = ExplosionRadius;
			if (radius <= 0f) return;

			int count = Physics.OverlapSphereNonAlloc(center, radius, _overlap, HitMaskValue, QueryTriggerInteraction.Ignore);
			_damaged.Clear();
			for (int i = 0; i < count; i++)
			{
				var col = _overlap[i];
				if (col == null) continue;
				var health = col.GetComponentInParent<Health>();
				if (health == null || health.IsAlive == false) continue;
				if (_damaged.Contains(health)) continue;
				_damaged.Add(health);

				// Linear falloff: 100% of ExplosionDamage at the centre → MinDamageFraction at the rim.
				// Distance measured to the nearest point on the body's bounds so big targets aren't
				// under-rewarded by a foot-anchored pivot.
				Vector3 nearest = col.bounds.ClosestPoint(center);
				float t = Mathf.Clamp01(Vector3.Distance(center, nearest) / radius);
				float frac = 1f - t * (1f - MinDamageFraction);
				int dmg = Mathf.Max(1, Mathf.RoundToInt(ExplosionDamage * frac));

				if (health.TakeHit(dmg, Attacker) && KnockbackDistance > 0f)
				{
					var knockable = health.GetComponentInParent<IKnockbackable>();
					knockable?.ApplyKnockback(center, KnockbackDistance * frac);
				}
			}
		}

		private void OnDetonatedChanged()
		{
			if (Detonated == false) return;
			if (_visual != null) _visual.SetActive(false);
			if (_explosionPrefab != null)
				Instantiate(_explosionPrefab, NetPosition, Quaternion.identity);
		}
	}
}
