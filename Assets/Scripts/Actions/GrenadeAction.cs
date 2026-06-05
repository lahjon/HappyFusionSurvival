using Fusion;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Throws a networked <see cref="GrenadeProjectile"/> on a ballistic arc. Consumes one unit from the
	/// holder's selected hotbar slot per throw (grenade-stack pattern — leave <see cref="AmmoItem"/> null),
	/// or from the first slot holding the configured ammo when set. The grenade deals no contact damage;
	/// it detonates after <see cref="FuseSeconds"/> and damages everything inside <see cref="ExplosionRadius"/>
	/// with linear falloff from the inherited <see cref="CombatAction.Damage"/> (centre, 100%) down to
	/// <see cref="MinDamageFraction"/> of it at the rim. Mirrors <see cref="ProjectileAction"/>'s throw + ammo
	/// path, but spawns the fuse-and-blast grenade instead of a contact projectile (ProjectileAction is
	/// sealed around a hardcoded <see cref="Projectile"/>, so the small ammo/spawn logic is restated here).
	/// </summary>
	[CreateAssetMenu(menuName = "Combat/Grenade Action", fileName = "Action_Grenade")]
	public sealed class GrenadeAction : CombatAction
	{
		[Header("Throw")]
		[Tooltip("Networked prefab spawned each throw. Must have NetworkObject + GrenadeProjectile.")]
		public GameObject GrenadePrefab;

		[Tooltip("Initial throw speed along the aim direction in m/s.")]
		public float ThrowSpeed = 14f;

		[Tooltip("Constant downward acceleration (m/s²) applied to the grenade in flight — the lob arc. ~18 reads as a hand-thrown toss.")]
		public float ThrowDropRate = 18f;

		[Tooltip("Distance ahead of the FireTransform where the grenade spawns, pushing it past the thrower's own collider on frame zero.")]
		public float SpawnForwardOffset = 0.5f;

		[Tooltip("Speed multiplier on a fully-charged throw (only used when Charge is enabled). 1 = no charge-throw.")]
		public float ChargedSpeedMultiplier = 1.8f;

		[Header("Fuse & Explosion")]
		[Tooltip("Seconds from the throw until the grenade detonates.")]
		public float FuseSeconds = 3f;

		[Tooltip("Blast radius in metres. Falloff is measured from the detonation point out to this distance.")]
		public float ExplosionRadius = 6f;

		[Tooltip("Fraction of the centre damage dealt at the very edge of the radius. 0.3 = 30% at the rim, 100% at the centre.")]
		[Range(0f, 1f)] public float MinDamageFraction = 0.3f;

		[Header("Ammo")]
		[Tooltip("Optional. Null = consume one from the holder's selected slot (the grenade stack itself). When set, the first slot holding this item is decremented instead.")]
		public ItemData AmmoItem;

		// Rough hint so AI keeps targets within lobbing distance; not an exact ballistic range.
		public override float EffectiveRange => ThrowSpeed;

		public override bool CanExecute(in ActorContext ctx)
		{
			// Runs on every predicting peer — Slots is networked, so IA and SA gate identically. Skip the
			// check for actors with no inventory (AI / dummies).
			if (ctx.AttackerRoot == null) return true;
			var inv = ctx.AttackerRoot.GetComponent<Inventory>();
			if (inv == null) return true;
			return FindAmmoSlot(inv) >= 0;
		}

		public override ActionHit Execute(in ActorContext ctx, bool charged)
		{
			var result = new ActionHit();
			if (ctx.Runner == null || ctx.FireTransform == null) return result;
			if (GrenadePrefab == null) return result;

			// Predicted ammo decrement — Slots is [Networked], so calling RemoveAt on IA writes a predicted
			// value that reconciles to SA's authoritative write; the HUD ticks down the instant of the throw.
			if (ctx.AttackerRoot != null)
			{
				var inv = ctx.AttackerRoot.GetComponent<Inventory>();
				if (inv != null)
				{
					int slot = FindAmmoSlot(inv);
					if (slot < 0) return result;
					inv.RemoveAt(slot, 1);
				}
			}

			// SA-only spawn — Runner.Spawn is authoritative in this project's host/client mode. Clients see
			// the grenade arrive ~1 RTT later; cooldown + throw feedback predict locally via ActionInvoker.
			if (ctx.IsStateAuthority == false) return result;

			ResolveCharged(charged, out int damage, out float knockback);
			float chargeT = Charge.Enabled ? Mathf.Clamp01(ctx.ChargeNormalized) : 0f;
			float speed = ThrowSpeed * Mathf.Lerp(1f, ChargedSpeedMultiplier, chargeT);

			Vector3 forward = ctx.FireTransform.forward;
			Vector3 spawnPos = ctx.FireTransform.position + forward * SpawnForwardOffset;
			Quaternion spawnRot = Quaternion.LookRotation(forward);
			Vector3 velocity = forward * speed;

			int hitMaskValue = HitMask.value;
			int dmg = damage;
			float kb = knockback;
			float drop = ThrowDropRate;
			float fuse = FuseSeconds;
			float radius = ExplosionRadius;
			float minFrac = MinDamageFraction;
			PlayerRef attacker = ctx.IgnoreAuthority;

			ctx.Runner.Spawn(
				GrenadePrefab,
				spawnPos,
				spawnRot,
				attacker,
				(runner, obj) =>
				{
					if (obj.TryGetComponent<GrenadeProjectile>(out var grenade))
					{
						grenade.AuthorityInitialize(runner, velocity, drop, fuse, dmg, radius, minFrac, kb, hitMaskValue, attacker);
					}
				});

			return result;
		}

		private int FindAmmoSlot(Inventory inv)
		{
			if (AmmoItem != null && AmmoItem.Id != 0)
			{
				for (int i = 0; i < inv.Slots.Length; i++)
				{
					var s = inv.Slots[i];
					if (s.IsEmpty) continue;
					if (s.ItemId == AmmoItem.Id) return i;
				}
				return -1;
			}

			int sel = inv.SelectedSlot;
			if (sel < 0 || sel >= inv.Slots.Length) return -1;
			return inv.Slots[sel].IsEmpty ? -1 : sel;
		}
	}
}
