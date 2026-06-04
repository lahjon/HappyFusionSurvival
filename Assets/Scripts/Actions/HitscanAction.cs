using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Instant-hit raycast attack. Uses Fusion's lag compensation so the predicting
	/// peer's aim resolves against rewound hitboxes on the state authority.
	///
	/// Supports a single bullet (pistol, rifle) or a multi-pellet shotgun blast
	/// (<see cref="PelletCount"/> &gt; 1): each pellet gets its own deterministic spread
	/// direction and rolls damage independently, so a point-blank hit stacks every pellet
	/// while a distant target is grazed by a few. Damage falls off with hit distance when
	/// <see cref="FalloffEndDistance"/> is configured.
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
		[Tooltip("Number of rays fired per activation. 1 = single bullet (pistol/rifle). >1 = shotgun: each pellet " +
			"gets its own spread direction inside the cone and deals Damage (after falloff) independently, so all " +
			"pellets can stack on a close target. Needs HipSpreadDegrees > 0 to actually scatter.")]
		[Min(1)] public int PelletCount = 1;
		[Tooltip("Hip-fire spread cone half-angle in degrees. 0 = pinpoint.")]
		public float HipSpreadDegrees = 0f;
		[Tooltip("Spread multiplier applied while the attacker is aiming (ADS). e.g. 0.2 = 80% tighter than hip.")]
		[Range(0f, 1f)] public float AimSpreadMultiplier = 0.2f;

		[Header("Damage Falloff")]
		[Tooltip("Distance (m) within which a hit deals full Damage. Beyond it, damage lerps down toward " +
			"MinDamageMultiplier by FalloffEndDistance. Falloff is OFF unless FalloffEndDistance > this value.")]
		[Min(0f)] public float FalloffStartDistance = 0f;
		[Tooltip("Distance (m) at and beyond which a hit deals only MinDamageMultiplier of Damage. Set <= " +
			"FalloffStartDistance (the default 0) to disable falloff — every hit deals full Damage.")]
		[Min(0f)] public float FalloffEndDistance = 0f;
		[Tooltip("Damage fraction a hit deals at (and past) FalloffEndDistance. 1 = no falloff; 0.25 = a quarter of " +
			"Damage at long range. A connecting hit always deals at least 1.")]
		[Range(0f, 1f)] public float MinDamageMultiplier = 1f;

		public override float EffectiveRange => Range;

		private bool FalloffEnabled => FalloffEndDistance > FalloffStartDistance && MinDamageMultiplier < 1f;

		public override ActionHit Execute(in ActorContext ctx, bool charged)
		{
			ResolveCharged(charged, out int damage, out float knockback);
			var result = new ActionHit();

			if (ctx.FireTransform == null || ctx.Runner == null)
				return result;

			Vector3 origin = ctx.FireTransform.position;
			Vector3 baseForward = ctx.FireTransform.forward;
			bool knockbackSpent = false;

			int pellets = Mathf.Max(1, PelletCount);
			for (int p = 0; p < pellets; p++)
			{
				Vector3 forward = ApplySpread(baseForward, in ctx, p);
				FirePellet(origin, forward, damage, knockback, in ctx, ref result, ref knockbackSpent);
			}

			return result;
		}

		// Resolves a single pellet: lag-compensated ray, falloff-scaled damage, knockback (once per blast).
		// Aggregates into the shared ActionHit — the first damaging pellet becomes the representative hit for
		// FX replication (impact point/normal), later pellets only OR in a kill.
		private void FirePellet(Vector3 origin, Vector3 forward, int damage, float knockback,
			in ActorContext ctx, ref ActionHit result, ref bool knockbackSpent)
		{
			if (ctx.Runner.LagCompensation.Raycast(
				    origin, forward, Range,
				    ctx.IgnoreAuthority, out var hit, HitMask, HitOptions, QueryTriggerInteraction.Ignore) == false)
				return;

			// Registered Fusion hitboxes (players, lag-compensated) resolve Health via hit.Hitbox.Root.
			// Targets with only a PhysX collider — e.g. the training dummy, whose Fusion hitbox isn't baked —
			// resolve via the collider hierarchy, mirroring OverlapAction.ResolveHealth so the same things are
			// damageable by gun and melee alike.
			Health health = null;
			if (hit.Hitbox != null)
				health = hit.Hitbox.Root.GetComponent<Health>();
			else if (hit.Collider != null)
				health = hit.Collider.GetComponentInParent<Health>();

			int finalDamage = ApplyFalloff(damage, Vector3.Distance(origin, hit.Point));
			var region = BodyHitbox.From(hit.Hitbox);
			if (region != null) finalDamage = region.Apply(finalDamage);

			if (health != null && health.TakeHit(finalDamage, ctx.IgnoreAuthority, hit.Point, hit.Normal))
			{
				if (result.DidHit == false)
				{
					result.DidHit = true;
					result.Target = health;
					result.Point = hit.Point;
					result.Normal = hit.Normal;
				}
				result.KilledTarget |= health.IsAlive == false;

				// One knockback impulse per blast — N pellets shouldn't launch a target N× as far.
				if (knockback > 0f && knockbackSpent == false)
				{
					var knockable = health.GetComponentInParent<IKnockbackable>();
					knockable?.ApplyKnockback(ctx.AttackerPosition, knockback);
					knockbackSpent = true;
				}
			}
			else if (health == null && hit.Collider != null && knockback > 0f && knockbackSpent == false)
			{
				// PhysX hit with no Health — e.g. a pickup item. Push it (once).
				var knockable = hit.Collider.GetComponentInParent<IKnockbackable>();
				if (knockable != null)
				{
					knockable.ApplyKnockback(ctx.AttackerPosition, knockback);
					knockbackSpent = true;
				}
			}
		}

		// Full damage out to FalloffStartDistance, then linearly down to MinDamageMultiplier at FalloffEndDistance
		// (and clamped there beyond). A connecting hit never drops below 1 so a grazing pellet still registers.
		private int ApplyFalloff(int damage, float distance)
		{
			if (FalloffEnabled == false) return damage;
			float t = Mathf.InverseLerp(FalloffStartDistance, FalloffEndDistance, distance);
			float mult = Mathf.Lerp(1f, MinDamageMultiplier, t);
			return Mathf.Max(1, Mathf.RoundToInt(damage * mult));
		}

		// Tilts the fire direction inside a cone whose half-angle is HipSpreadDegrees (× AimSpreadMultiplier
		// while aiming). The RNG is seeded from the simulation tick + attacker + pellet index so the input
		// authority's predicted ray and the state authority's authoritative ray resolve to the SAME direction —
		// without this, ADS hit prediction would diverge from the server and rubber-band. The pellet index keeps
		// each shotgun pellet on its own deterministic-but-distinct trajectory; pellet 0 of a single-shot weapon
		// reproduces the original pinpoint seed exactly.
		private Vector3 ApplySpread(Vector3 forward, in ActorContext ctx, int pelletIndex)
		{
			float half = HipSpreadDegrees * (ctx.IsAiming ? AimSpreadMultiplier : 1f);
			if (half <= 0f || ctx.Runner == null) return forward;

			int seed = unchecked(((int)ctx.Runner.Tick * 6151) ^ (ctx.IgnoreAuthority.PlayerId * 24593) ^ (pelletIndex * 3079));
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
