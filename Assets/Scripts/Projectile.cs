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

		// HitOptions mirrors HitscanAction: ignore the attacker's input authority + include
		// PhysX colliders (for stationary props / surfaces that don't have a Fusion Hitbox).
		private const HitOptions Hits = HitOptions.IncludePhysX | HitOptions.IgnoreInputAuthority;

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
				if (Runner.LagCompensation.Raycast(fromPos, dir, dist, Attacker, out var hit,
					    HitMaskValue, Hits, QueryTriggerInteraction.Ignore))
				{
					// Land at the hit point and freeze. ResolveImpact handles damage + pickup spawn
					// + despawn, so we don't touch NetPosition past this point on this tick.
					NetPosition = hit.Point;
					Vector3 facing = vel.sqrMagnitude > 0.0001f ? vel.normalized : dir;
					NetRotation = Quaternion.LookRotation(facing);
					Velocity = Vector3.zero;
					ResolveImpact(hit, facing);
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

		private void ResolveImpact(LagCompensatedHit hit, Vector3 facing)
		{
			// Damage path — only when the raycast hit an actual Fusion Hitbox with a Health component.
			// PhysX-only hits (walls, props) skip this branch and just stick the pickup.
			if (hit.Hitbox != null)
			{
				var health = hit.Hitbox.Root.GetComponent<Health>();
				if (health != null && health.TakeHit(Damage, Attacker))
				{
					if (KnockbackDistance > 0f)
					{
						var knockable = hit.Hitbox.Root.GetComponent<IKnockbackable>();
						knockable?.ApplyKnockback(NetPosition, KnockbackDistance);
					}
				}
			}

			// Spawn the pickup at the impact point so the projectile can be retrieved. The pickup's
			// WorldPrefab is the same one used for inventory drops — pickup/stacking logic is shared.
			if (ItemId != 0 && ItemDatabase.Instance != null)
			{
				var def = ItemDatabase.Instance.GetById(ItemId);
				if (def != null && def.WorldPrefab != null
					&& def.WorldPrefab.GetComponent<NetworkObject>() != null)
				{
					Quaternion stickRot = Quaternion.LookRotation(facing);
					var spawned = Runner.Spawn(def.WorldPrefab, NetPosition, stickRot, Attacker);
					if (spawned != null && spawned.TryGetComponent<PickupableItem>(out var pi))
					{
						pi.Initialize(ItemId, 1);
						pi.Stick(NetPosition, stickRot);
					}
				}
			}

			if (_impactPrefab != null)
			{
				Instantiate(_impactPrefab, NetPosition, Quaternion.LookRotation(hit.Normal != Vector3.zero ? hit.Normal : -facing));
			}

			Runner.Despawn(Object);
		}
	}
}
