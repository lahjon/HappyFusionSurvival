using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Represents a spawning point in the environment. Tag it with the <see cref="Zone"/> it sits inside
	/// (<see cref="ZoneId"/>) so teams spawn at Day start inside their own zone; every spawn point should fall
	/// within exactly one zone.
	/// </summary>
	public class SpawnPoint : MonoBehaviour
	{
		public float Radius = 1f;

		[Tooltip("Id of the Zone this spawn point sits inside. -1 = not tagged (usable by any team as a fallback).")]
		public int ZoneId = -1;

		private void OnDrawGizmosSelected()
		{
			Gizmos.color = ZoneId >= 0 ? Zone.ColorForId(ZoneId) : Color.white;
			Gizmos.DrawWireSphere(transform.position, Radius);
		}
	}
}
