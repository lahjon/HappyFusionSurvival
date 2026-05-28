using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Flying chicken: launched from <see cref="ChickenSpawner"/> with a fixed speed
	/// and travel limit, kills itself on overlap. Shared NPC plumbing (Health,
	/// NetworkTransform, knockback) lives on <see cref="NPC"/>.
	/// </summary>
	public class Chicken : NPC
	{
		[Header("Visuals")]
		public ParticleSystem FlyParticles;

		// Start position, speed and max travel distance values do not need
		// to be networked as it is used only on the state authority.
		private Vector3 _startPosition;
		private float _speed;
		private float _maxTravelDistance;

		public void Respawn(Vector3 position, Quaternion rotation, float speed, float maxTravelDistance)
		{
			Health.Revive();

			_startPosition = position;
			_speed = speed;
			_maxTravelDistance = maxTravelDistance;

			NetworkTransform.Teleport(position, rotation);
		}

		protected override void OnFixedUpdateAlive()
		{
			if (Vector3.Distance(_startPosition, transform.position) > _maxTravelDistance)
			{
				// Chicken is too far, kill itself
				Health.TakeHit(1000);
				return;
			}

			// Move the chicken. Position/rotation are synchronized via NetworkTransform.
			transform.Translate(Vector3.forward * _speed * Runner.DeltaTime, Space.Self);
		}

		public override void Render()
		{
			var emission = FlyParticles.emission;
			emission.enabled = Health.IsAlive;
		}

		private void OnTriggerEnter(Collider other)
		{
			// Chickens are destroyed only on state authority
			// - on clients the OnTriggerEnter/Exit calls
			// are not reliable due to resimulations
			if (HasStateAuthority == false)
				return;

			// Chicken collided, let's destroy it
			Health.TakeHit(1000);
		}
	}
}
