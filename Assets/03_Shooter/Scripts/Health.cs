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

		public bool IsAlive => CurrentHealth > 0;
		public bool IsFinished => IsAlive == false && _deathCooldown.Expired(Runner);

		[Networked, HideInInspector, OnChangedRender(nameof(OnCurrentHealthChanged))]
		public int CurrentHealth { get; set; }

		[Networked]
		private TickTimer _deathCooldown { get; set; }

		public bool TakeHit(int damage)
		{
			if (IsAlive == false)
				return false;

			CurrentHealth -= damage;

			if (IsAlive == false)
			{
				// Entity died, let's start death cooldown
				CurrentHealth = 0;
				_deathCooldown = TickTimer.CreateFromSeconds(Runner,  DeathTime);
			}

			return true;
		}

		public void Revive()
		{
			CurrentHealth = InitialHealth;
			_deathCooldown = default;
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
