using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Spawns flying chickens in the environment.
	/// Notice that nothing in this spawner actually needs to be networked
	/// as all the logic is on the state authority only.
	/// </summary>
	public class ChickenSpawner : NetworkBehaviour
	{
		public Chicken ChickenPrefab;
		public int Count = 30;
		public float SpawnRadius = 50f;
		public float SpawnHeightMin = 0f;
		public float SpawnHeightMax = 20f;
		public float SpeedMin = 5f;
		public float SpeedMax = 15f;
		public float DirectionDispersion = 10f;

		private List<Chicken> _chickens = new(128);

		public override void Spawned()
		{
			if (HasStateAuthority == false)
				return;

			// On start just show all chickens
			for (int i = 0; i < Count; i++)
			{
				var chicken = Runner.Spawn(ChickenPrefab, Vector3.zero, Quaternion.identity);
				_chickens.Add(chicken);

				Respawn(chicken);
			}
		}

		public override void FixedUpdateNetwork()
		{
			for (int i = 0; i < _chickens.Count; i++)
			{
				var chicken = _chickens[i];

				if (chicken.Health.IsFinished)
				{
					Respawn(chicken);
				}
			}
		}

		private void Respawn(Chicken chicken)
		{
			var circlePosition = Random.insideUnitCircle.normalized * SpawnRadius;
			var position = new Vector3(circlePosition.x, Random.Range(SpawnHeightMin, SpawnHeightMax), circlePosition.y);

			var rotationToCenter = Quaternion.LookRotation(transform.position - position);
			var randomDispersion = Random.insideUnitSphere * DirectionDispersion;
			var rotation = Quaternion.Euler(0f, randomDispersion.y, randomDispersion.z) * rotationToCenter;

			float speed = Random.Range(SpeedMin, SpeedMax);

			chicken.Respawn(position, rotation, speed, SpawnRadius * 2.5f);
		}

		private void OnDrawGizmosSelected()
		{
			Gizmos.DrawWireSphere(transform.position, SpawnRadius);
		}
	}
}
