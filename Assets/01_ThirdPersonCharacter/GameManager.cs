using UnityEngine;
using Fusion;

namespace Starter.ThirdPersonCharacter
{
	/// <summary>
	/// Handles player connections (spawning of Player instances).
	/// </summary>
	public sealed class GameManager : NetworkBehaviour, IPlayerJoined, IPlayerLeft
	{
		public NetworkObject PlayerPrefab;
		public float SpawnRadius = 3f;

		public void PlayerJoined(PlayerRef playerRef)
		{
			if (HasStateAuthority == false)
				return;

			var randomPositionOffset = Random.insideUnitCircle * SpawnRadius;
			var spawnPosition = transform.position + new Vector3(randomPositionOffset.x, transform.position.y, randomPositionOffset.y);

			var player = Runner.Spawn(PlayerPrefab, spawnPosition, Quaternion.identity, playerRef);
			Runner.SetPlayerObject(playerRef, player);
		}

		public void PlayerLeft(PlayerRef playerRef)
		{
			if (HasStateAuthority == false)
				return;

			var player = Runner.GetPlayerObject(playerRef);
			if (player != null)
			{
				Runner.Despawn(player);
			}
		}

		private void OnDrawGizmosSelected()
		{
			Gizmos.DrawWireSphere(transform.position, SpawnRadius);
		}
	}
}
