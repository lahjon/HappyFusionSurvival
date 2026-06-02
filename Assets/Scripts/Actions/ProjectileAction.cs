using Fusion;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Spawns a networked <see cref="Projectile"/> that integrates ballistically toward the target
	/// using <see cref="ProjectileSpeed"/> and <see cref="ProjectileDropRate"/>. Consumes one unit
	/// from the holder's inventory on each fire — from the selected slot when <see cref="AmmoItem"/>
	/// is null (throwing knife / shuriken pattern), or from the first slot containing the configured
	/// ammo when set (bow-and-arrow pattern). The projectile's damage / knockback / hit mask are
	/// inherited from <see cref="CombatAction"/>, so the same primitive scales to any thrown or
	/// launched weapon.
	/// </summary>
	[CreateAssetMenu(menuName = "Combat/Projectile Action", fileName = "Action_Projectile")]
	public sealed class ProjectileAction : CombatAction
	{
		[Header("Projectile")]
		[Tooltip("Networked prefab spawned each time the action fires. Must have NetworkObject + Projectile.")]
		public GameObject ProjectilePrefab;

		[Tooltip("Initial speed along the fire direction in m/s.")]
		public float ProjectileSpeed = 30f;

		[Tooltip("Constant downward acceleration (m/s²) applied to the projectile in flight. 0 = laser-straight, ~15 = noticeable arc for a thrown knife.")]
		public float ProjectileDropRate = 15f;

		[Tooltip("Failsafe lifetime (seconds) — projectile despawns if it never hits anything.")]
		public float MaxLifetime = 4f;

		[Tooltip("Distance ahead of the FireTransform where the projectile spawns. Pushes it past the camera so it doesn't intersect the player's own collider on frame zero.")]
		public float SpawnForwardOffset = 0.4f;

		[Header("Charge (bow-style)")]
		[Tooltip("Speed multiplier applied to a fully-charged release. Used by Charge-enabled actions (e.g. drawing a bow). Damage/knockback charge multipliers are inherited from CombatAction.Charge.")]
		public float ChargedSpeedMultiplier = 2f;

		[Header("Ammo")]
		[Tooltip("Optional. When null, consumes one unit from the holder's selected hotbar slot (throwing-knife pattern). When set, the holder must have at least one of this item in any slot — the first matching stack is decremented (bow-and-arrow pattern).")]
		public ItemDefinition AmmoItem;

		public override float EffectiveRange
		{
			get
			{
				// Ballistic range estimate: how far the projectile travels before drop pulls it below
				// the launch height. With launch velocity v0 along forward and gravity g, range ≈ v0² / g.
				// AI uses this to decide when to fire; not exact but a sensible upper bound.
				if (ProjectileDropRate <= 0f) return ProjectileSpeed * MaxLifetime;
				return (ProjectileSpeed * ProjectileSpeed) / ProjectileDropRate;
			}
		}

		public override bool CanExecute(in ActorContext ctx)
		{
			// Runs on every predicting peer — networked Slots are visible to both IA and SA so the
			// gate resolves identically. Skip when the attacker has no inventory (AI / dummy).
			if (ctx.AttackerRoot == null) return true;
			var inv = ctx.AttackerRoot.GetComponent<Inventory>();
			if (inv == null) return true;
			// Magazine-fed weapons (e.g. the sniper) gate firing through the magazine cycle in
			// Player.Fire, not this action's inventory ammo — let those always pass here.
			if (inv.ActiveUsesMagazine) return true;
			return FindAmmoSlot(inv) >= 0;
		}

		public override ActionHit Execute(in ActorContext ctx, bool charged)
		{
			var result = new ActionHit();
			if (ctx.Runner == null || ctx.FireTransform == null) return result;
			if (ProjectilePrefab == null) return result;

			// Predicted ammo decrement. Inventory.Slots is [Networked] — calling RemoveAt on both
			// IA and SA writes a predicted value on IA that reconciles to SA's authoritative write,
			// so the HUD ticks down the moment the throw is initiated rather than after 1 RTT.
			//
			// Magazine-fed weapons (sniper, etc.) are the exception: their ammo is consumed by the
			// Player magazine cycle (Player.Fire → Inventory.TryConsumeChamberedRound), so this action
			// must NOT also decrement inventory. It also leaves itemIdFired = 0 so the spawned bullet
			// plants no retrievable pickup on impact — fired rounds aren't picked back up like arrows.
			short itemIdFired = 0;
			if (ctx.AttackerRoot != null)
			{
				var inv = ctx.AttackerRoot.GetComponent<Inventory>();
				if (inv != null && inv.ActiveUsesMagazine == false)
				{
					int slot = FindAmmoSlot(inv);
					if (slot < 0) return result;
					itemIdFired = inv.Slots[slot].ItemId;
					inv.RemoveAt(slot, 1);
				}
			}

			// SA-only — Runner.Spawn is authoritative in this project's host/client mode. Clients
			// see the projectile arrive ~1 RTT after the throw; cooldown + audio + recoil predict
			// locally via ActionInvoker.Cooldown / Player._fireCount so the press still feels instant.
			if (ctx.IsStateAuthority == false) return result;

			ResolveCharged(charged, out int damage, out float knockback);
			// Analog charge: launch speed ramps from base (tap) to base * ChargedSpeedMultiplier (held to
			// the charge threshold), proportional to how long the throw was charged.
			float chargeT = Charge.Enabled ? Mathf.Clamp01(ctx.ChargeNormalized) : 0f;
			float speed = ProjectileSpeed * Mathf.Lerp(1f, ChargedSpeedMultiplier, chargeT);

			Vector3 forward = ctx.FireTransform.forward;
			Vector3 spawnPos = ctx.FireTransform.position + forward * SpawnForwardOffset;
			Quaternion spawnRot = Quaternion.LookRotation(forward);
			Vector3 velocity = forward * speed;

			short ammoId = itemIdFired;
			int hitMaskValue = HitMask.value;
			int dmg = damage;
			float kb = knockback;
			float drop = ProjectileDropRate;
			float life = MaxLifetime;
			PlayerRef attacker = ctx.IgnoreAuthority;

			ctx.Runner.Spawn(
				ProjectilePrefab,
				spawnPos,
				spawnRot,
				attacker,
				(runner, obj) =>
				{
					if (obj.TryGetComponent<Projectile>(out var proj))
					{
						proj.AuthorityInitialize(runner, velocity, drop, dmg, kb, hitMaskValue, attacker, ammoId, life);
					}
				});

			return result;
		}

		private int FindAmmoSlot(Inventory inv)
		{
			if (AmmoItem != null && AmmoItem.Id != 0)
			{
				// Bow-style: scan every slot for the configured ammo item.
				for (int i = 0; i < inv.Slots.Length; i++)
				{
					var s = inv.Slots[i];
					if (s.IsEmpty) continue;
					if (s.ItemId == AmmoItem.Id) return i;
				}
				return -1;
			}

			// Knife-style: consume from whatever the player has selected.
			int sel = inv.SelectedSlot;
			if (sel < 0 || sel >= inv.Slots.Length) return -1;
			return inv.Slots[sel].IsEmpty ? -1 : sel;
		}
	}
}
