using Fusion;
using Starter.Shooter;
using UnityEngine;

namespace Starter.Hunger
{
	/// <summary>
	/// Self-contained networked hunger (fullness) stat — the survival mechanic lifted out of <c>Player</c> during the
	/// Purge × Stardew pivot so it can be re-enabled later without re-entangling core movement code.
	///
	/// DORMANT BY DEFAULT: this component is intentionally NOT on the Player prefab. Add it to the Player NetworkObject
	/// (then let Fusion re-bake) to switch hunger back on; <see cref="FoodCapability"/> and <see cref="HungerBarUI"/>
	/// detect it automatically. With no HungerSystem present, eating food no-ops and the bar hides — exactly today's
	/// behavior.
	///
	/// Networked-first: state authority owns <see cref="Hunger"/> and drains it in FixedUpdateNetwork; every peer reads
	/// the replicated value. Phase-aware: refills on Day entry via <see cref="MatchManager.PhaseChanged"/> (the single
	/// networked phase source), never local <c>Time.time</c>. Decoupled from <c>Player</c> — it touches only a sibling
	/// <c>Health</c> (optional) to pause draining while dead.
	/// </summary>
	public sealed class HungerSystem : NetworkBehaviour
	{
		[Tooltip("Maximum fullness. Hunger starts here on spawn / round reset and is the cap restored by food.")]
		public float MaxHunger = 100f;

		[Tooltip("Hunger drained per 5 seconds passively (constant background tick). At MaxHunger=100 and 1/5s that's " +
			"~8.3 minutes to starve at rest. Set to 0 to disable passive drain.")]
		public float HungerDrainPer5Seconds = 1f;

		[Tooltip("Reserved: extra hunger per point of stamina spent — the 'work makes you hungrier' nudge. NOT applied " +
			"while the module is decoupled (it no longer reaches into Player's stamina). Re-wire to a stamina source if " +
			"survival returns.")]
		public float HungerPerStaminaPoint = 0.005f;

		/// <summary>Current fullness in [0, MaxHunger]. Only state authority writes; every peer reads.</summary>
		[Networked]
		public float Hunger { get; private set; }

		// Optional sibling — cached so passive drain can pause while the body is dead/downed-out. Null is fine.
		private Health _health;

		public override void Spawned()
		{
			_health = GetComponent<Health>();

			if (HasStateAuthority)
				Hunger = MaxHunger;

			MatchManager.PhaseChanged += OnPhaseChanged;
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			MatchManager.PhaseChanged -= OnPhaseChanged;
		}

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false) return;
			if (HungerDrainPer5Seconds <= 0f) return;
			if (_health != null && _health.IsAlive == false) return; // no drain while dead

			Hunger = Mathf.Max(0f, Hunger - HungerDrainPer5Seconds * Runner.DeltaTime / 5f);
		}

		/// <summary>Restore fullness (food). Safe to call on every predicting peer — only state authority mutates.</summary>
		public void AddHunger(float amount)
		{
			if (HasStateAuthority == false || amount <= 0f) return;
			Hunger = Mathf.Min(MaxHunger, Hunger + amount);
		}

		/// <summary>Refill to <see cref="MaxHunger"/> (round reset). State-authority only.</summary>
		public void ResetFullness()
		{
			if (HasStateAuthority == false) return;
			Hunger = MaxHunger;
		}

		// Round-based refill: players revive on Day entry (no per-death respawn), so top hunger up then.
		private void OnPhaseChanged(MatchPhase phase)
		{
			if (phase == MatchPhase.Day)
				ResetFullness();
		}
	}
}
