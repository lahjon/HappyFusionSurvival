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

		public bool IsAlive => CurrentHealth > 0;
		public bool IsFinished => IsAlive == false && _deathCooldown.Expired(Runner);

		[Networked, HideInInspector, OnChangedRender(nameof(OnCurrentHealthChanged))]
		public int CurrentHealth { get; set; }

		[Networked]
		private TickTimer _deathCooldown { get; set; }

		/// <summary>Environmental / unattributed damage (falls, AI w/ no PlayerRef). Bypasses the PvP gate;
		/// callers that know the attacker should use the (damage, attacker) overload instead.</summary>
		public bool TakeHit(int damage) => TakeHit(damage, PlayerRef.None);

		// Runs on every predicting peer. Returning true on input auth lets the caller
		// drive predicted hit FX, but only state auth mutates CurrentHealth — predicting
		// the decrement would double-fire OnCurrentHealthChanged when the snapshot reconciles.
		public bool TakeHit(int damage, PlayerRef attacker)
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
			if (TryGetComponent<Player>(out _) == false) return false;

			var match = MatchManager.Instance;
			if (match == null) return false;

			// Day / DuskWarning / Lobby / MatchOver → no PvP allowed.
			if (match.Phase != MatchPhase.Night) return true;

			// Night: friendly fire off.
			var team = TeamManager.Instance;
			return team != null && team.SameTeam(attacker, Object.InputAuthority);
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
