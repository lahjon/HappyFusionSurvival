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

		public override float EffectiveRange => Range;

		public override ActionHit Execute(in ActorContext ctx, bool charged)
		{
			ResolveCharged(charged, out int damage, out float knockback);
			var result = new ActionHit();

			if (ctx.FireTransform == null || ctx.Runner == null)
				return result;

			Vector3 origin = ctx.FireTransform.position;
			Vector3 forward = ctx.FireTransform.forward;

			if (ctx.Runner.LagCompensation.Raycast(
				    origin, forward, Range,
				    ctx.IgnoreAuthority, out var hit, HitMask, HitOptions, QueryTriggerInteraction.Ignore))
			{
				result.Point = hit.Point;
				result.Normal = hit.Normal;

				var health = hit.Hitbox != null ? hit.Hitbox.Root.GetComponent<Health>() : null;
				if (health != null && health.TakeHit(damage))
				{
					result.DidHit = true;
					result.Target = health;
					if (knockback > 0f)
					{
						var knockable = hit.Hitbox.Root.GetComponent<IKnockbackable>();
						knockable?.ApplyKnockback(ctx.AttackerPosition, knockback);
					}
					result.KilledTarget = health.IsAlive == false;
				}
			}

			return result;
		}
	}
}
