using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Sphere-overlap melee attack. Finds Health components in a radius around the
	/// FireTransform and applies damage + knockback. Used by melee weapons (bat),
	/// fists, and AI punchers (TrainingDummy).
	/// </summary>
	[CreateAssetMenu(menuName = "Combat/Overlap Action", fileName = "Action_Overlap")]
	public sealed class OverlapAction : CombatAction
	{
		[Header("Overlap")]
		[Tooltip("Radius of the overlap sphere in meters.")]
		public float Range = 2f;
		[Tooltip("If true, only the single closest target is damaged. If false, every overlapping target is hit (cleave).")]
		public bool SingleTarget = true;

		private static readonly Collider[] _buffer = new Collider[16];

		public override float EffectiveRange => Range;

		public override ActionHit Execute(in ActorContext ctx, bool charged)
		{
			ResolveCharged(charged, out int damage, out float knockback);
			var result = new ActionHit();

			if (ctx.FireTransform == null)
				return result;

			Vector3 center = ctx.FireTransform.position;
			int count = Physics.OverlapSphereNonAlloc(
				center, Range, _buffer, HitMask, QueryTriggerInteraction.Ignore);

			if (count == 0) return result;

			if (SingleTarget)
			{
				Health closest = null;
				float closestSqr = float.MaxValue;
				for (int i = 0; i < count; i++)
				{
					var health = ResolveHealth(_buffer[i], ctx.AttackerRoot);
					if (health == null) continue;
					float sqr = (health.transform.position - ctx.AttackerPosition).sqrMagnitude;
					if (sqr < closestSqr)
					{
						closest = health;
						closestSqr = sqr;
					}
				}

				ApplyHit(closest, damage, knockback, ctx.AttackerPosition, ctx.IgnoreAuthority, ref result);
			}
			else
			{
				for (int i = 0; i < count; i++)
				{
					var health = ResolveHealth(_buffer[i], ctx.AttackerRoot);
					ApplyHit(health, damage, knockback, ctx.AttackerPosition, ctx.IgnoreAuthority, ref result);
				}
			}

			// Push IKnockbackable-only targets (pickups, props) regardless of SingleTarget —
			// these have no Health so they don't conflict with the closest-target damage pick,
			// and we want every nearby item to react to the swing.
			if (knockback > 0f)
			{
				for (int i = 0; i < count; i++)
				{
					var col = _buffer[i];
					if (col == null) continue;
					if (col.GetComponentInParent<Health>() != null) continue;
					var knockable = col.GetComponentInParent<IKnockbackable>();
					if (knockable == null) continue;
					if (ctx.AttackerRoot != null && (knockable as Component)?.gameObject == ctx.AttackerRoot) continue;
					knockable.ApplyKnockback(ctx.AttackerPosition, knockback);
				}
			}

			return result;
		}

		private static Health ResolveHealth(Collider col, GameObject attackerRoot)
		{
			if (col == null) return null;
			var health = col.GetComponentInParent<Health>();
			if (health == null) return null;
			if (health.IsAlive == false) return null;
			// Skip self even when the layer mask is permissive.
			if (attackerRoot != null && health.gameObject == attackerRoot) return null;
			return health;
		}

		private static void ApplyHit(Health health, int damage, float knockback, Vector3 from, PlayerRef attacker, ref ActionHit result)
		{
			if (health == null) return;
			if (health.TakeHit(damage, attacker) == false) return;

			if (knockback > 0f)
			{
				var knockable = health.GetComponentInParent<IKnockbackable>();
				knockable?.ApplyKnockback(from, knockback);
			}

			// First hit becomes the "representative" hit for FX replication.
			if (result.DidHit == false)
			{
				result.DidHit = true;
				result.Target = health;
				result.Point = health.transform.position;
				result.KilledTarget = health.IsAlive == false;
			}
		}
	}
}
