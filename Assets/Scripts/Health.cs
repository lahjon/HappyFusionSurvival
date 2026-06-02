using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// A common component that represents entity health.
	/// It is used for both players and chickens.
	/// </summary>
	public class Health : NetworkBehaviour
	{
		[Header("Setup")]
		public int InitialHealth = 3;
		public float DeathTime;

		[Header("References")]
		public Transform ScalingRoot;
		public GameObject VisualRoot;
		public GameObject DeathRoot;

		[Header("Impact Reaction")]
		[Tooltip("VFX spawned (local-only) on every peer at the impact point whenever this entity takes a hit. Use a short-lived prefab (e.g. with a DestroyAfter). Leave null for no spawned VFX.")]
		public GameObject ImpactEffect;
		[Tooltip("Optional Animator on which a trigger is fired on every peer when this entity is hit (e.g. a flinch/hurt animation).")]
		public Animator ImpactAnimator;
		[Tooltip("Trigger parameter raised on ImpactAnimator each hit. Empty = no trigger fired.")]
		public string ImpactTrigger = "Hit";
		[Tooltip("Sound played at the impact point on every peer when this entity is hit.")]
		public AudioClip ImpactClip;
		[Range(0f, 1f)] public float ImpactVolume = 1f;

		/// <summary>Set by entities that draw their own death visual (e.g. ragdolled player) so
		/// the default VisualRoot/DeathRoot swap is skipped and the body stays visible past death.</summary>
		[System.NonSerialized] public bool SuppressDeathVisualSwap;

		/// <summary>External "no damage" flag (e.g. set by <see cref="Player"/> while sleeping). Authority's
		/// TakeHit short-circuits when true. Not networked — every peer that calls TakeHit must keep this
		/// in sync with whatever gameplay state should grant invulnerability.</summary>
		[System.NonSerialized] public bool IsInvulnerable;

		/// <summary>State-authority-only callback fired by <see cref="TakeHit"/> just before HP would
		/// hit zero. If the hook returns true the lethal damage is absorbed: HP is clamped to 1 and the
		/// death cooldown does NOT start. Used by <see cref="Player"/> to enter the downed state instead
		/// of dying outright. Set null on entities that should die normally (chickens etc.).</summary>
		[System.NonSerialized] public System.Func<bool> AuthorityDownHook;

		/// <summary>Details of a single impact, handed to <see cref="OnImpactReaction"/> on every peer.</summary>
		public readonly struct ImpactInfo
		{
			/// <summary>World-space point of impact (falls back to the entity's position for point-less hits like melee).</summary>
			public readonly Vector3 Point;
			/// <summary>Surface normal at the impact; zero when the resolver didn't supply one.</summary>
			public readonly Vector3 Normal;
			/// <summary>Damage that landed this hit.</summary>
			public readonly int Damage;

			public ImpactInfo(Vector3 point, Vector3 normal, int damage)
			{
				Point = point;
				Normal = normal;
				Damage = damage;
			}
		}

		/// <summary>Per-entity, code-driven impact reaction invoked on EVERY peer when this entity takes a hit
		/// (after the designer-wired VFX/animation/sound). Set in Spawned() by entities that need a bespoke
		/// reaction the inspector fields can't express (material flash, hit scream, etc.). Local-only — runs
		/// inside <see cref="OnImpact"/>, so don't mutate networked state here.</summary>
		[System.NonSerialized] public System.Action<ImpactInfo> OnImpactReaction;

		public bool IsAlive => CurrentHealth > 0;
		public bool IsFinished => IsAlive == false && _deathCooldown.Expired(Runner);

		[Networked, HideInInspector, OnChangedRender(nameof(OnCurrentHealthChanged))]
		public int CurrentHealth { get; set; }

		[Networked]
		private TickTimer _deathCooldown { get; set; }

		// Impact replication. State authority records where/how hard the last hit landed and bumps
		// _impactCount; the OnChangedRender fires OnImpact() on every peer. Counter + OnChangedRender
		// (not RPC) is the canonical "one-shot visual all peers must see" pattern — it tolerates
		// dropped ticks, and a counter reacts even when two hits land on the same tick.
		[Networked] private Vector3 _lastImpactPoint { get; set; }
		[Networked] private Vector3 _lastImpactNormal { get; set; }
		[Networked] private int _lastImpactDamage { get; set; }

		[Networked, OnChangedRender(nameof(OnImpact))]
		private int _impactCount { get; set; }

		/// <summary>Environmental / unattributed damage (falls, AI w/ no PlayerRef). Bypasses the PvP gate;
		/// callers that know the attacker should use the (damage, attacker) overload instead.</summary>
		public bool TakeHit(int damage) => TakeHit(damage, PlayerRef.None, Vector3.zero, Vector3.zero);

		/// <summary>Attributed damage with no precise impact location (the entity's own position is used for
		/// the impact reaction). Melee/overlap hits use this — they have no surface point or normal.</summary>
		public bool TakeHit(int damage, PlayerRef attacker) => TakeHit(damage, attacker, Vector3.zero, Vector3.zero);

		// Runs on every predicting peer. Returning true on input auth lets the caller
		// drive predicted hit FX, but only state auth mutates CurrentHealth — predicting
		// the decrement would double-fire OnCurrentHealthChanged when the snapshot reconciles.
		/// <param name="hitPoint">World-space impact point for the impact reaction; pass Vector3.zero when
		/// the resolver has no precise point (the entity's position is substituted).</param>
		/// <param name="hitNormal">Surface normal at the impact; Vector3.zero when unknown.</param>
		public bool TakeHit(int damage, PlayerRef attacker, Vector3 hitPoint, Vector3 hitNormal)
		{
			if (IsAlive == false)
				return false;
			if (IsInvulnerable)
				return false;

			// PvP rules: only player-vs-player damage is gated. Environmental / AI damage (attacker == None)
			// and damage to non-players (chickens, dummies, props) always passes through. Checked on every
			// predicting peer so client-side hit FX prediction matches the host's outcome.
			if (PvpDamageBlocked(attacker))
				return false;

			if (HasStateAuthority == false)
				return true;

			CurrentHealth -= damage;

			// Record this impact and bump the counter so OnImpact() fires on every peer (VFX / flinch
			// animation / sound). Done for every landed hit, including the lethal one below, so a killing
			// blow still kicks off its impact reaction.
			_lastImpactPoint = hitPoint == Vector3.zero ? transform.position : hitPoint;
			_lastImpactNormal = hitNormal;
			_lastImpactDamage = damage;
			_impactCount++;

			// Feed the Purge director: landing PvP damage on another player during Night marks the attacker
			// as active, so aggressive players are never flagged passive / picked as the hunter.
			if (attacker != PlayerRef.None
				&& MatchManager.Instance != null
				&& MatchManager.Instance.Phase == MatchPhase.Night
				&& TryGetComponent<Player>(out _))
			{
				GameHostManager.Instance?.RecordCombat(attacker);
			}

			if (IsAlive == false)
			{
				bool isPlayerTarget = TryGetComponent<Player>(out _);

				// Give the host-side owner a chance to absorb the lethal blow (Player uses this
				// to enter the downed state). If the hook handles it, clamp HP back to 1 so the
				// entity remains "alive" for the rest of the gameplay logic; the downed flow owns
				// the bleed-out timer separately. If no hook or the hook declines, fall through to
				// the normal death cooldown.
				if (AuthorityDownHook != null && AuthorityDownHook())
				{
					CurrentHealth = 1;
				}
				else
				{
					// Entity died, let's start death cooldown
					CurrentHealth = 0;
					_deathCooldown = TickTimer.CreateFromSeconds(Runner,  DeathTime);
				}

				// Credit the elimination to the attacker's team even when the down-hook caught the blow —
				// putting another player into downed is the kill event for tiebreaker scoring. Only counts
				// during Night, only when a real player struck a real player.
				if (isPlayerTarget
					&& attacker != PlayerRef.None
					&& MatchManager.Instance != null
					&& MatchManager.Instance.Phase == MatchPhase.Night)
				{
					TeamManager.Instance?.RegisterKill(attacker);
				}
			}

			return true;
		}

		/// <summary>True when player-vs-player damage should be blocked under the current match phase /
		/// team rules. Non-player targets and unattributed damage (PlayerRef.None) always pass.</summary>
		private bool PvpDamageBlocked(PlayerRef attacker)
		{
			if (attacker == PlayerRef.None) return false;
			if (TryGetComponent<Player>(out var victim) == false) return false;

			var match = MatchManager.Instance;
			if (match == null) return false;

			// Day / DuskWarning / Lobby / MatchOver → no PvP allowed, unless the `arm` debug command
			// has force-enabled combat (IsPvpForced) for testing.
			if (match.Phase != MatchPhase.Night && match.IsPvpForced == false) return true;

			// PvP active (Night or debug-armed): friendly fire off. Key on the victim's stable Owner
			// (synthetic for bots) so bot teams get the same friendly-fire protection humans do.
			var team = TeamManager.Instance;
			return team != null && team.SameTeam(attacker, victim.Owner);
		}

		/// <summary>State-authority-only path that drops HP straight to zero and starts the death
		/// cooldown, bypassing <see cref="AuthorityDownHook"/>. Used for non-damage kills — the
		/// downed bleed-out timer calls this when it expires so the hook doesn't catch the kill
		/// and loop the player back into downed.</summary>
		public void AuthorityKill()
		{
			if (HasStateAuthority == false) return;
			if (IsAlive == false) return;

			CurrentHealth = 0;
			_deathCooldown = TickTimer.CreateFromSeconds(Runner, DeathTime);
		}

		public void Revive()
		{
			if (HasStateAuthority == false) return;
			CurrentHealth = InitialHealth;
			_deathCooldown = default;
		}

		public void Heal(int amount)
		{
			if (HasStateAuthority == false) return;
			if (IsAlive == false || amount <= 0) return;

			CurrentHealth = Mathf.Min(InitialHealth, CurrentHealth + amount);
		}

		public override void Spawned()
		{
			if (HasStateAuthority)
			{
				// Set initial health
				CurrentHealth = InitialHealth;
			}
		}

		public override void Render()
		{
			if (SuppressDeathVisualSwap)
				return;

			// Use interpolated value when checking if entity is alive in Render.
			// This will ensure that death effects are played AFTER the death was "confirmed"
			// on the server in case of mispredictions (e.g. lost fire input) and also helps
			// with showing player visual at the correct position right away after respawn
			// (= player won't be visible before KCC teleport that is interpolated as well).
			var interpolator = new NetworkBehaviourBufferInterpolator(this);
			bool isAlive = interpolator.Int(nameof(CurrentHealth)) > 0;

			VisualRoot.SetActive(isAlive);
			DeathRoot.SetActive(isAlive == false);
		}

		// Fired on EVERY peer (state auth, input auth, proxies) when state authority bumps _impactCount,
		// i.e. whenever this entity takes a hit. Everything here is local-only view: spawn impact VFX,
		// raise a flinch/hurt animation trigger, play a sound, and invoke the per-entity code hook. Each
		// entity opts in by wiring the inspector fields and/or setting OnImpactReaction in Spawned().
		private void OnImpact()
		{
			Vector3 point = _lastImpactPoint;
			Vector3 normal = _lastImpactNormal;
			Quaternion rotation = normal != Vector3.zero ? Quaternion.LookRotation(normal) : Quaternion.identity;

			if (ImpactEffect != null)
				Instantiate(ImpactEffect, point, rotation);

			if (ImpactAnimator != null && string.IsNullOrEmpty(ImpactTrigger) == false)
				ImpactAnimator.SetTrigger(ImpactTrigger);

			if (ImpactClip != null)
				AudioSource.PlayClipAtPoint(ImpactClip, point, ImpactVolume);

			OnImpactReaction?.Invoke(new ImpactInfo(point, normal, _lastImpactDamage));
		}

		private void OnCurrentHealthChanged()
		{
			if (CurrentHealth <= 0)
				return; // Just health reset

			if (HasInputAuthority == false && ScalingRoot != null)
			{
				// Show hit reaction by simple scale. Scaling root
				// scale is lerped back to one in the Player script.
				ScalingRoot.localScale = new Vector3(0.85f, 1.15f, 0.85f);
			}
		}
	}
}
