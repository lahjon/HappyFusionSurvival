using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Host-driven spawner for a single NPC type. Spawns <see cref="NpcDefinition.Prefab"/>
	/// on the state authority, injects scene refs (movement area, patrol path) via
	/// <see cref="NpcAgent.Initialize"/>, and adds/removes the NPCs as the match phase
	/// enters/leaves <see cref="NpcDefinition.ActivePhases"/> (e.g. townsfolk only in Day).
	///
	/// Scene-placed on a NetworkObject. All spawning is gated on state authority, mirroring
	/// the <see cref="GameManager"/> player-spawn pattern.
	/// </summary>
	public sealed class NpcSpawner : NetworkBehaviour
	{
		[Header("What to spawn")]
		public NpcDefinition Definition;
		[Tooltip("How many of this NPC to spawn while active.")]
		[Min(1)] public int Count = 1;

		[Header("Where")]
		[Tooltip("Spawn anchors. One NPC per index, cycling if Count exceeds the list. Falls back to this transform when empty.")]
		public Transform[] SpawnPoints;

		[Header("AI scene references")]
		[Tooltip("Movement area injected into every spawned NPC (Wander/leash).")]
		public NpcMovementArea Area;
		[Tooltip("Patrol path injected into every spawned NPC (Patrol behavior).")]
		public NpcPatrolPath Path;

		private readonly List<NpcAgent> _spawned = new List<NpcAgent>();
		private bool _active;

		public override void Spawned()
		{
			MatchManager.PhaseChanged += OnPhaseChanged;
			if (HasStateAuthority)
				Evaluate(MatchManager.Instance != null ? MatchManager.Instance.Phase : MatchPhase.Lobby);
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			MatchManager.PhaseChanged -= OnPhaseChanged;
			if (HasStateAuthority) DespawnAll();
		}

		private void OnPhaseChanged(MatchPhase phase)
		{
			if (HasStateAuthority) Evaluate(phase);
		}

		private void Evaluate(MatchPhase phase)
		{
			if (Definition == null) return;

			bool shouldExist = Definition.IsActiveInPhase(phase);
			if (shouldExist && _active == false) SpawnAll();
			else if (shouldExist == false && _active) DespawnAll();
		}

		private void SpawnAll()
		{
			if (Definition.Prefab == null)
			{
				Debug.LogWarning($"[NpcSpawner] '{name}' has no Prefab on its NpcDefinition — nothing to spawn.", this);
				return;
			}

			for (int i = 0; i < Count; i++)
			{
				Transform anchor = ResolveAnchor(i);
				var npc = Runner.Spawn(
					Definition.Prefab, anchor.position, anchor.rotation, null,
					(runner, obj) =>
					{
						var agent = obj.GetComponent<NpcAgent>();
						if (agent != null) agent.Initialize(Definition, Area, Path);
					});

				if (npc != null) _spawned.Add(npc);
			}

			_active = true;
		}

		private void DespawnAll()
		{
			for (int i = 0; i < _spawned.Count; i++)
			{
				var npc = _spawned[i];
				if (npc != null && npc.Object != null) Runner.Despawn(npc.Object);
			}
			_spawned.Clear();
			_active = false;
		}

		private Transform ResolveAnchor(int index)
		{
			if (SpawnPoints != null && SpawnPoints.Length > 0)
				return SpawnPoints[index % SpawnPoints.Length];
			return transform;
		}
	}
}
