using System.Collections.Generic;
using Fusion;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Scene-placed, host-driven spawner for world pickups. Each <see cref="SpawnPoints"/> anchor
	/// holds one live <see cref="PickupableItem"/> rolled from the weighted <see cref="LootEntry"/>
	/// table. Spawning is state-authority only (mirrors <see cref="NpcSpawner"/> / <see cref="GameManager"/>);
	/// the spawned pickups replicate themselves to every peer.
	///
	/// Two modes (<see cref="RespawnAfterPickup"/>):
	/// <list type="bullet">
	/// <item>OFF — one-shot: fill every anchor once when the spawner becomes active, despawn leftovers when it goes inactive.</item>
	/// <item>ON — gathering node: when an anchor's pickup is taken, refill it after <see cref="RespawnDelay"/> seconds.</item>
	/// </list>
	///
	/// Optionally phase-gated via <see cref="ActivePhases"/> (e.g. loot only during Day), reading the
	/// networked <see cref="MatchManager"/> phase exactly like <see cref="NpcSpawner"/> — never a local clock.
	/// Reuses <see cref="LootRoll"/> for the weighted pick and the item's bespoke
	/// <see cref="ItemDefinition.WorldPrefab"/> override (falling back to the shared generic pickup),
	/// the same resolution <see cref="LootDropper"/> uses.
	/// </summary>
	public sealed class ItemSpawner : NetworkBehaviour
	{
		[Header("What to spawn")]
		[Tooltip("Candidate items. Each anchor rolls one entry, weighted by Weight (Weight <= 0 entries are only " +
		         "used as a fallback when every weight is zero).")]
		[SerializeField] private List<LootEntry> _lootTable = new();

		[Header("Where")]
		[Tooltip("Spawn anchors — one live pickup per anchor. Falls back to this transform when empty.")]
		[SerializeField] private Transform[] _spawnPoints;
		[Tooltip("Vertical offset (m) above each anchor where the pickup is placed.")]
		[SerializeField] private float _spawnHeight = 0.1f;

		[Header("Prefab")]
		[Tooltip("Shared world-pickup prefab (NetworkObject + PickupableItem) used for any item that has no bespoke " +
		         "WorldPrefab override on its ItemDefinition. Wire Pickup_Generic here.")]
		[SerializeField] private GameObject _genericPickupPrefab;

		[Header("Respawn")]
		[Tooltip("ON: refill an anchor a few seconds after its pickup is taken (gathering node). OFF: one-shot fill, " +
		         "no refill.")]
		[SerializeField] private bool _respawnAfterPickup = true;
		[Tooltip("Seconds after a pickup is taken before its anchor is refilled (RespawnAfterPickup only).")]
		[SerializeField, Min(0f)] private float _respawnDelay = 30f;

		[Header("Phase Awareness")]
		[Tooltip("Match phases during which this spawner is active. Empty = always active. Leaving an active phase " +
		         "despawns everything; re-entering refills.")]
		[SerializeField] private MatchPhase[] _activePhases;

		// Per-anchor live pickup + refill timer. Index aligns with the resolved anchor list (SpawnPoints, or
		// a single entry for this.transform). Host-only state — clients never run the spawn loop.
		private NetworkObject[] _live;
		private TickTimer[] _refillTimer;
		private bool _active;

		public override void Spawned()
		{
			MatchManager.PhaseChanged += OnPhaseChanged;
			if (HasStateAuthority)
			{
				int anchors = (_spawnPoints != null && _spawnPoints.Length > 0) ? _spawnPoints.Length : 1;
				_live = new NetworkObject[anchors];
				_refillTimer = new TickTimer[anchors];
				Evaluate(MatchManager.Instance != null ? MatchManager.Instance.Phase : MatchPhase.Lobby);
			}
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
			bool shouldExist = IsActiveInPhase(phase);
			if (shouldExist && _active == false) Activate();
			else if (shouldExist == false && _active) DespawnAll();
		}

		private bool IsActiveInPhase(MatchPhase phase)
		{
			if (_activePhases == null || _activePhases.Length == 0) return true;
			for (int i = 0; i < _activePhases.Length; i++)
				if (_activePhases[i] == phase) return true;
			return false;
		}

		// Fill every anchor immediately on becoming active.
		private void Activate()
		{
			_active = true;
			for (int i = 0; i < _live.Length; i++)
			{
				_refillTimer[i] = default;
				SpawnAt(i);
			}
		}

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false || _active == false) return;

			for (int i = 0; i < _live.Length; i++)
			{
				// Anchor still has its pickup — nothing to do.
				if (_live[i] != null && _live[i].IsValid) continue;

				// Pickup was taken/despawned. One-shot mode leaves the anchor empty forever.
				_live[i] = null;
				if (_respawnAfterPickup == false) continue;

				// Start the refill countdown the first tick we notice the anchor is empty.
				if (_refillTimer[i].IsRunning == false)
				{
					_refillTimer[i] = TickTimer.CreateFromSeconds(Runner, _respawnDelay);
					continue;
				}

				if (_refillTimer[i].Expired(Runner))
				{
					_refillTimer[i] = default;
					SpawnAt(i);
				}
			}
		}

		// Host-only. Roll the table and spawn one pickup at anchor <paramref name="index"/>.
		private void SpawnAt(int index)
		{
			if (Runner == null || ItemDatabase.Instance == null) return;
			if (_lootTable == null || _lootTable.Count == 0) return;
			if (!LootRoll.TryPickWeighted(_lootTable, out var entry) || entry.Item == null) return;

			var prefab = ResolvePickupPrefab(entry.Item);
			if (prefab == null) return;

			Transform anchor = ResolveAnchor(index);
			Vector3 pos = anchor.position + Vector3.up * _spawnHeight;

			var spawned = Runner.Spawn(prefab, pos, anchor.rotation);
			if (spawned == null) return;
			if (spawned.TryGetComponent<PickupableItem>(out var pi) == false)
			{
				Runner.Despawn(spawned);
				return;
			}

			pi.Initialize(entry.Item.Id, entry.Count > 0 ? entry.Count : (short)1);
			_live[index] = spawned;
		}

		private void DespawnAll()
		{
			if (_live != null)
			{
				for (int i = 0; i < _live.Length; i++)
				{
					if (_live[i] != null && _live[i].IsValid) Runner.Despawn(_live[i]);
					_live[i] = null;
					_refillTimer[i] = default;
				}
			}
			_active = false;
		}

		private Transform ResolveAnchor(int index)
		{
			if (_spawnPoints != null && _spawnPoints.Length > 0)
				return _spawnPoints[index % _spawnPoints.Length];
			return transform;
		}

		/// <summary>Item's bespoke WorldPrefab if it sets one, else the shared generic pickup. Null if neither resolves.</summary>
		private GameObject ResolvePickupPrefab(ItemDefinition def)
		{
			if (def == null) return null;

			var prefab = def.WorldPrefab != null ? def.WorldPrefab : _genericPickupPrefab;
			if (prefab == null)
			{
				Debug.LogWarning($"[ItemSpawner] '{name}': item '{def.DisplayName}' has no WorldPrefab and no " +
				                 "generic pickup prefab is assigned — skipped.", this);
				return null;
			}
			if (prefab.GetComponent<NetworkObject>() == null)
			{
				Debug.LogWarning($"[ItemSpawner] '{name}': pickup prefab '{prefab.name}' has no NetworkObject — skipped.", this);
				return null;
			}
			return prefab;
		}
	}
}
