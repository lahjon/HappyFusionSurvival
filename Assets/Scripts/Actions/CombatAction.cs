using System;
using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	public enum EFeedbackStyle : byte
	{
		Ranged = 0, // muzzle particle + recoil kick on the holder
		Melee  = 1, // swing/punch animation on the holder
	}

	[Serializable]
	public class ChargeProfile
	{
		[Tooltip("If false, the action fires immediately on press and ignores the rest of these knobs.")]
		public bool Enabled = false;
		[Tooltip("Seconds the attack button must be held before release applies the charged multipliers.")]
		public float ThresholdSeconds = 2f;
		[Tooltip("Damage multiplier applied when released after holding past the threshold.")]
		public float DamageMultiplier = 2f;
		[Tooltip("Knockback distance multiplier applied to a fully-charged release.")]
		public float KnockbackMultiplier = 2f;
	}

	/// <summary>
	/// Authoritative attack definition. Holds tuning (damage, cooldown, knockback,
	/// hit mask, audio, charge profile) and a resolver (Execute) that runs on state
	/// authority to find targets and apply damage/knockback. Attach instances to
	/// HeldWeapon, FistPunchAnimator, or any AI authoring component via their
	/// Actions list — the executor (ActionInvoker) does not care which.
	/// </summary>
	public abstract class CombatAction : ScriptableObject
	{
		[Header("Damage")]
		public int Damage = 1;
		[Tooltip("Minimum seconds between activations of this action on the same invoker. 0 = no cooldown.")]
		public float Cooldown = 0.5f;
		[Tooltip("Distance (meters) the target is pushed away from the attacker. 0 = no knockback.")]
		public float KnockbackDistance = 0f;

		/// <summary>Shared layer mask for every combat action's hit query. All current actions (pistol, bat,
		/// punch, peck, throwing knife) target the same set of layers — using a static keeps a forgotten
		/// per-asset HitMask=0 from silently making projectiles pass through the world. Edit here if you
		/// add a new layer that combat queries should respect.</summary>
		public static readonly LayerMask HitMask = 1; // Default layer

		[Header("Feedback")]
		[Tooltip("Drives visual hook on the holder: Ranged plays muzzle + recoil, Melee plays swing/punch.")]
		public EFeedbackStyle Style = EFeedbackStyle.Ranged;
		[Tooltip("Sound played when this action fires. Played on the holder's AudioSource.")]
		public AudioClip AttackClip;
		[Range(0f, 1f)] public float AttackVolume = 1f;
		[Tooltip("Optional VFX spawned (local-only) at the impact point when this action hits. Lets each weapon have its own impact (spark, dust, splash). Falls back to the holder's global ImpactPrefab when unset. Use a short-lived prefab — e.g. one with a DestroyAfter component.")]
		public GameObject ImpactEffect;

		[Header("Charge")]
		public ChargeProfile Charge = new ChargeProfile();

		/// <summary>
		/// Range hint used by AI to decide whether to attempt firing this action.
		/// Subclasses override; defaults to 0 (no range gating).
		/// </summary>
		public virtual float EffectiveRange => 0f;

		/// <summary>
		/// Cheap pre-fire gate evaluated on every predicting peer before <see cref="Execute"/>
		/// runs. Returning false aborts the fire (no cooldown, no feedback, no _fireCount++).
		/// Used by ammo-consuming actions (ProjectileAction) to block dry-fires when the stack
		/// is empty — Slots is networked, so IA and SA agree without an extra RPC.
		/// </summary>
		public virtual bool CanExecute(in ActorContext ctx) => true;

		/// <summary>
		/// Resolve targets, apply damage/knockback. Runs on every predicting peer (input
		/// authority + state authority) so input auth can drive predicted hit FX from the
		/// returned <see cref="ActionHit"/>. Side effects that mutate networked state
		/// (Health.CurrentHealth, knockback timers) gate themselves on HasStateAuthority.
		/// </summary>
		public abstract ActionHit Execute(in ActorContext ctx, bool charged);

		protected void ResolveCharged(bool charged, out int damage, out float knockback)
		{
			damage = Damage;
			knockback = KnockbackDistance;
			if (charged && Charge.Enabled)
			{
				damage = Mathf.Max(1, Mathf.RoundToInt(damage * Charge.DamageMultiplier));
				knockback *= Charge.KnockbackMultiplier;
			}
		}
	}

	/// <summary>
	/// Per-firing context passed to <see cref="CombatAction.Execute"/>. Held lightly
	/// so actions don't reach back into the holder's component graph.
	/// </summary>
	public struct ActorContext
	{
		public NetworkRunner Runner;
		/// <summary>Passed to LagCompensation.Raycast as the predicting player; default for AI.</summary>
		public PlayerRef IgnoreAuthority;
		/// <summary>Used as the "push-from" point for knockback (typically the attacker's transform.position).</summary>
		public Vector3 AttackerPosition;
		/// <summary>Position + forward direction for hitscan; position only for overlap.</summary>
		public Transform FireTransform;
		/// <summary>Optional — the attacker's root GameObject. Used to skip self when an action's HitMask overlaps the attacker's layer.</summary>
		public GameObject AttackerRoot;
		/// <summary>True iff the caller is the state authority for the attacker. Predicting peers (IA) see false. Actions that spawn NetworkObjects (e.g. projectiles) gate the Runner.Spawn call on this.</summary>
		public bool IsStateAuthority;
		/// <summary>0..1 charge amount at release (chargeSeconds / Charge.ThresholdSeconds, clamped). 0 for an uncharged/instant fire. ProjectileAction scales launch speed by this; melee still uses the binary <c>charged</c> flag for its damage multiplier.</summary>
		public float ChargeNormalized;
		/// <summary>True iff the attacker is holding the aim stance (ADS) this fire. HitscanAction tightens its spread cone when set. Always false for AI / non-aiming weapons.</summary>
		public bool IsAiming;
	}

	/// <summary>
	/// Result of a single Execute() call. Plumbed back through ActionInvoker so the
	/// caller can replicate visuals (hit point/normal) and apply per-actor side effects.
	/// </summary>
	public struct ActionHit
	{
		/// <summary>True when the action actually fired (cooldown ok and resolver ran).</summary>
		public bool DidFire;
		/// <summary>True when the resolver successfully damaged at least one target.</summary>
		public bool DidHit;
		/// <summary>Health component of the (first) target hit. May be null even when DidHit is true if the target had no Health (shouldn't normally happen).</summary>
		public Health Target;
		/// <summary>True iff the target died from this hit.</summary>
		public bool KilledTarget;
		/// <summary>World-space point of impact, for impact-FX replication. Vector3.zero when nothing was hit.</summary>
		public Vector3 Point;
		/// <summary>Surface normal at the impact point. Zero when nothing was hit.</summary>
		public Vector3 Normal;
	}
}
