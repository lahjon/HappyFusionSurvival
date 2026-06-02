using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Instant-hit raycast attack. Uses Fusion's lag compensation so the predicting
	/// peer's aim resolves against rewound hitboxes on the state authority.
	/// </summary>
	[CreateAssetMenu(menuName = "Combat/Hitscan Action", fileName = "Action_Hitscan")]
	public sealed class HitscanAction : CombatAction
	{
		[Header("Hitscan")]
		[Tooltip("Maximum ray length in meters.")]
		public float Range = 200f;
		[Tooltip("Lag-compensated raycast options. IncludePhysX + IgnoreInputAuthority mirrors the original Player.Fire behavior.")]
		public HitOptions HitOptions = HitOptions.IncludePhysX | HitOptions.IgnoreInputAuthority;

		[Header("Spread")]
		[Tooltip("Hip-fire spread cone half-angle in degrees. 0 = pinpoint.")]
		public float HipSpreadDegrees = 0f;
		[Tooltip("Spread multiplier applied while the attacker is aiming (ADS). e.g. 0.2 = 80% tighter than hip.")]
		[Range(0f, 1f)] public float AimSpreadMultiplier = 0.2f;

		public override float EffectiveRange => Range;

		public override ActionHit Execute(in ActorContext ctx, bool charged)
		{
			ResolveCharged(charged, out int damage, out float knockback);
			var result = new ActionHit();

			if (ctx.FireTransform == null || ctx.Runner == null)
				return result;

			Vector3 origin = ctx.FireTransform.position;
			Vector3 forward = ApplySpread(ctx.FireTransform.forward, in ctx);

			if (ctx.Runner.LagCompensation.Raycast(
				    origin, forward, Range,
				    ctx.IgnoreAuthority, out var hit, HitMask, HitOptions, QueryTriggerInteraction.Ignore))
			{
				result.Point = hit.Point;
				result.Normal = hit.Normal;

				// Registered Fusion hitboxes (players, lag-compensated) resolve Health via
				// hit.Hitbox.Root. Targets with only a PhysX collider — e.g. the training
				// dummy, whose Fusion hitbox isn't baked — resolve via the collider hierarchy,
				// mirroring OverlapAction.ResolveHealth so the same things are damageable by
				// gun and melee alike.
				Health health = null;
				if (hit.Hitbox != null)
					health = hit.Hitbox.Root.GetComponent<Health>();
				else if (hit.Collider != null)
					health = hit.Collider.GetComponentInParent<Health>();

				int finalDamage = damage;
				var region = BodyHitbox.From(hit.Hitbox);
				if (region != null) finalDamage = region.Apply(damage);
				if (health != null && health.TakeHit(finalDamage, ctx.IgnoreAuthority, result.Point, result.Normal))
				{
					result.DidHit = true;
					result.Target = health;
					if (knockback > 0f)
					{
						var knockable = health.GetComponentInParent<IKnockbackable>();
						knockable?.ApplyKnockback(ctx.AttackerPosition, knockback);
					}
					result.KilledTarget = health.IsAlive == false;
				}
				else if (health == null && hit.Collider != null && knockback > 0f)
				{
					// PhysX hit with no Health — e.g. a pickup item. Push it.
					var knockable = hit.Collider.GetComponentInParent<IKnockbackable>();
					knockable?.ApplyKnockback(ctx.AttackerPosition, knockback);
				}
			}

			return result;
		}

		// Tilts the fire direction inside a cone whose half-angle is HipSpreadDegrees (× AimSpreadMultiplier
		// while aiming). The RNG is seeded from the simulation tick + attacker so the input authority's
		// predicted ray and the state authority's authoritative ray resolve to the SAME direction — without
		// this, ADS hit prediction would diverge from the server and rubber-band.
		private Vector3 ApplySpread(Vector3 forward, in ActorContext ctx)
		{
			float half = HipSpreadDegrees * (ctx.IsAiming ? AimSpreadMultiplier : 1f);
			if (half <= 0f || ctx.Runner == null) return forward;

			int seed = unchecked(((int)ctx.Runner.Tick * 6151) ^ (ctx.IgnoreAuthority.PlayerId * 24593));
			var rng = new System.Random(seed);
			float ang = Mathf.Sqrt((float)rng.NextDouble()) * half; // sqrt → area-uniform across the cone
			float az = (float)rng.NextDouble() * 360f;

			Quaternion look = Quaternion.LookRotation(forward);
			Quaternion spin = Quaternion.AngleAxis(az, Vector3.forward); // azimuth around the aim axis
			Quaternion tilt = Quaternion.Euler(ang, 0f, 0f);            // off-axis deflection
			return look * spin * tilt * Vector3.forward;
		}
	}
}
