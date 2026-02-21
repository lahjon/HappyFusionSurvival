using Fusion;
using UnityEngine;

namespace Starter.Platformer
{
	/// <summary>
	/// Coin object that can be picked up by player.
	/// </summary>
	[RequireComponent(typeof(Collider))]
	public class Coin : NetworkBehaviour
	{
		public float RefreshTime = 4f;
		public Collider Trigger;
		public GameObject VisualRoot;
		public ParticleSystem Particles;

		[Networked]
		private TickTimer _activationCooldown { get; set; }

		public void Collect()
		{
			if (HasStateAuthority == false)
				return;

			Trigger.enabled = false;
			_activationCooldown  = TickTimer.CreateFromSeconds(Runner, RefreshTime);
		}

		public override void Spawned()
		{
			// Coins are collected only on the state authority
			Trigger.enabled = HasStateAuthority;
		}

		public override void FixedUpdateNetwork()
		{
			Trigger.enabled = _activationCooldown.ExpiredOrNotRunning(Runner);
		}

		public override void Render()
		{
			bool isActive = _activationCooldown.ExpiredOrNotRunning(Runner);

			// Show/hide coin visual
			VisualRoot.SetActive(isActive);

			// Start/stop particles emission
			var emission = Particles.emission;
			emission.enabled = isActive;
		}
	}
}
