using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	public enum EHitRegion : byte
	{
		Body  = 0,
		Head  = 1,
		Chest = 2,
		Arm   = 3,
		Leg   = 4,
	}

	/// <summary>
	/// Sits next to a Fusion <see cref="Hitbox"/> on each region of a body (head/chest/arm/leg).
	/// Damage resolvers (<see cref="HitscanAction"/>, <see cref="Projectile"/>) read the multiplier
	/// off the hit's Hitbox and scale the damage before forwarding to <see cref="Health.TakeHit"/>.
	/// </summary>
	[RequireComponent(typeof(Hitbox))]
	public sealed class BodyHitbox : MonoBehaviour
	{
		public EHitRegion Region = EHitRegion.Body;

		[Tooltip("Damage scale applied to incoming hits on this region. Final damage is rounded and clamped to at least 1.")]
		public float DamageMultiplier = 1f;

		public int Apply(int rawDamage)
		{
			if (DamageMultiplier <= 0f) return 0;
			return Mathf.Max(1, Mathf.RoundToInt(rawDamage * DamageMultiplier));
		}

		/// <summary>Look up the BodyHitbox on the given Fusion Hitbox. Returns null if the hit has no
		/// per-region tag — callers should treat that as a 1x multiplier (no headshot bonus).</summary>
		public static BodyHitbox From(Hitbox hitbox)
		{
			return hitbox != null ? hitbox.GetComponent<BodyHitbox>() : null;
		}
	}
}
