using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Main script representing flying chicken enemies.
	/// </summary>
	public class Chicken : NetworkBehaviour
	{
		[Header("References")]
		public Health Health;
		public NetworkTransform NetworkTransform;
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

		public override void FixedUpdateNetwork()
		{
			if (Health.IsAlive == false)
				return;

			if (Vector3.Distance(_startPosition, transform.position) > _maxTravelDistance)
			{
				// Chicken is too far, kill itself
				Health.TakeHit(1000);
				return;
			}

			// Move the chicken.
			// The chicken position and rotation is synchronized to all clients via NetworkTransform component.
			// Note: There is also a much more bandwidth efficient way when only start move parameters
			// are synchronized over the network and no NetworkTransform is necessary. It is a bit out of the scope
			// of this starter sample so check Projectile Essentials where same approach is explained for projectiles.
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
