using System.Collections.Generic;
using UnityEngine;
using Fusion;

namespace Starter.Shooter
{
	/// <summary>
	/// Handles player connections (spawning of Player instances).
	/// </summary>
	public sealed class GameManager : NetworkBehaviour, IPlayerJoined, IPlayerLeft
	{
		public Player PlayerPrefab;

		[Networked]
		public PlayerRef BestHunter { get; set; }
		public Player LocalPlayer { get; private set; }

		private List<Player> _players = new(32);
		private SpawnPoint[] _spawnPoints;

		public override void Spawned()
		{
			_spawnPoints = FindObjectsOfType<SpawnPoint>();
		}

		public override void FixedUpdateNetwork()
		{
			BestHunter = PlayerRef.None;
			int bestHunterKills = 0;

			for (int i = 0; i < _players.Count; i++)
			{
				var player = _players[i];

				if (player.KCC.Position.y < -15f)
				{
					// Player fell, let's kill him
					player.Health.TakeHit(1000);
				}

				if (player.Health.IsFinished)
				{
					player.Respawn(GetSpawnPosition());
				}

				// Calculate the best hunter
				if (player.Health.IsAlive && player.ChickenKills > bestHunterKills)
				{
					bestHunterKills = player.ChickenKills;
					BestHunter = player.Object.InputAuthority;
				}
			}
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			// Clear the reference because UI can try to access it even after despawn
			LocalPlayer = null;
		}

		public override void Render()
		{
			// Prepare LocalPlayer property that can be accessed from UI
			if (LocalPlayer == null || LocalPlayer.Object == null || LocalPlayer.Object.IsValid == false)
			{
				var playerObject = Runner.GetPlayerObject(Runner.LocalPlayer);
				LocalPlayer = playerObject != null ? playerObject.GetComponent<Player>() : null;
			}
		}

		public void PlayerJoined(PlayerRef playerRef)
		{
			if (HasStateAuthority == false)
				return;

			var player = Runner.Spawn(PlayerPrefab, GetSpawnPosition(), Quaternion.identity, playerRef);
			Runner.SetPlayerObject(playerRef, player.Object);

			// This list is state authority only,
			// so it is valid to have this list non-networked
			_players.Add(player);
		}

		public void PlayerLeft(PlayerRef playerRef)
		{
			if (HasStateAuthority == false)
				return;

			int index = _players.FindIndex(t => t.Object.InputAuthority == playerRef);
			if (index >= 0)
			{
				Runner.Despawn(_players[index].Object);
				_players.RemoveAt(index);
			}
		}

		private Vector3 GetSpawnPosition()
		{
			var spawnPoint = _spawnPoints[Random.Range(0, _spawnPoints.Length)];
			var randomPositionOffset = Random.insideUnitCircle * spawnPoint.Radius;
			return spawnPoint.transform.position + new Vector3(randomPositionOffset.x, 0f, randomPositionOffset.y);
		}
	}
}
