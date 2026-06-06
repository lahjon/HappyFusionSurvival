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

			// Resource nodes (trees, rocks) are harvested, not damaged: route the swing to the matching node
			// with the swinger's tool tag. A node never participates in the Health damage pick below, so the
			// closest-target logic can't be "absorbed" by a tree the player wasn't aiming to chop. Harvest
			// mutation is state-authority only; the returned ActionHit still drives swing feedback on the IA.
			HarvestNodes(in ctx, center, count, ref result);

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

				ApplyHit(closest, damage, knockback, ctx.AttackerPosition, ctx.IgnoreAuthority, AllowInPeacePhase, ref result);
			}
			else
			{
				for (int i = 0; i < count; i++)
				{
					var health = ResolveHealth(_buffer[i], ctx.AttackerRoot);
					ApplyHit(health, damage, knockback, ctx.AttackerPosition, ctx.IgnoreAuthority, AllowInPeacePhase, ref result);
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

		// Harvest every matching resource node the swing overlaps. SingleTarget doesn't apply: chopping a tree
		// shouldn't also "use up" the swing's combat target pick, so nodes are handled independently here and
		// excluded from ResolveHealth. Each node decides for itself whether the tool matches (wrong tool = no-op).
		private static void HarvestNodes(in ActorContext ctx, Vector3 center, int count, ref ActionHit result)
		{
			if (ctx.IsStateAuthority == false) return; // harvest mutates networked state; SA only

			for (int i = 0; i < count; i++)
			{
				var col = _buffer[i];
				if (col == null) continue;
				var node = col.GetComponentInParent<ResourceNode>();
				if (node == null) continue;

				if (node.TryHarvest(ctx.ToolTag, ctx.IgnoreAuthority) && result.DidHit == false)
				{
					result.DidHit = true;
					result.Point = node.transform.position;
				}
			}
		}

		private static Health ResolveHealth(Collider col, GameObject attackerRoot)
		{
			if (col == null) return null;
			// Resource nodes are harvested in HarvestNodes, never damaged via the combat path.
			if (col.GetComponentInParent<ResourceNode>() != null) return null;
			var health = col.GetComponentInParent<Health>();
			if (health == null) return null;
			if (health.IsAlive == false) return null;
			// Skip self even when the layer mask is permissive.
			if (attackerRoot != null && health.gameObject == attackerRoot) return null;
			return health;
		}

		private static void ApplyHit(Health health, int damage, float knockback, Vector3 from, PlayerRef attacker, bool shoveWhenUndamaged, ref ActionHit result)
		{
			if (health == null) return;

			bool damaged = health.TakeHit(damage, attacker);

			// Knockback normally rides on a landed hit. When the action is flagged to shove even with no damage
			// (the peace-phase punch — see CombatAction.AllowInPeacePhase), still push a living, vulnerable target:
			// the daytime PvP gate in Health.TakeHit blocks the damage but the punch should still nudge people.
			if (knockback > 0f && (damaged || (shoveWhenUndamaged && health.IsAlive && health.IsInvulnerable == false)))
			{
				var knockable = health.GetComponentInParent<IKnockbackable>();
				knockable?.ApplyKnockback(from, knockback);
			}

			if (damaged == false) return;

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
