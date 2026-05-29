using System.Collections.Generic;
using Fusion;
using Fusion.Addons.SimpleKCC;
using Starter.Common.Interactions;
using Starter.Shooter;
using UnityEngine;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// Networked world object representing a single dropped/lootable stack.
	/// State authority owns the optional Rigidbody simulation (throw arcs after drop);
	/// remote clients read the networked position so they see the same arc.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	public sealed class PickupableItem : NetworkBehaviour, IInteractable, IKnockbackable
	{
		[Header("Authoring (scene-placed pickups)")]
		[Tooltip("Used when a pickup is placed in the scene. Programmatic spawns call Initialize() instead.")]
		[SerializeField] private ItemDefinition _initialItem;
		[SerializeField, Min(1)] private short _initialCount = 1;

		[Header("Procedural Loot")]
		[Tooltip("Optional list of possible items. If non-empty, takes precedence over _initialItem.")]
		[SerializeField] private List<LootEntry> _lootTable;
		[Tooltip("ON: pick a weighted-random entry at spawn. OFF: take the first entry of the list.")]
		[SerializeField] private bool _randomize = true;

		[Header("Interaction")]
		[Tooltip("Max distance from the player at which this can be picked up.")]
		public float PickupRange = 2f;

		[Header("Player Push")]
		[Tooltip("Extra radius (m) beyond the item's collider used to detect player overlap for shove-out. SimpleKCC is kinematic, so PhysX never imparts velocity on contact — this is what makes the item move when you walk into it.")]
		[SerializeField] private float _playerPushRadius = 0.2f;
		[Tooltip("Baseline nudge speed (m/s) when a stationary player just touches the item.")]
		[SerializeField] private float _baselinePushSpeed = 0.5f;
		[Tooltip("Upper bound on the push speed (m/s) so a sprinting player can't kick items across the map.")]
		[SerializeField] private float _maxPushSpeed = 4f;

		[Header("Hit Response")]
		[Tooltip("Multiplier converting a CombatAction's KnockbackDistance (meters) into initial horizontal velocity (m/s) when shot or melee'd.")]
		[SerializeField] private float _knockbackImpulseScale = 4f;
		[Tooltip("Upward velocity (m/s) added per unit of KnockbackDistance, for a small toss arc.")]
		[SerializeField] private float _knockbackUpScale = 2f;

		[Networked] public short ItemId { get; set; }
		[Networked] public short Count { get; set; }
		[Networked] public Vector3 NetPosition { get; set; }
		[Networked] public Quaternion NetRotation { get; set; }
		[Networked] public TickTimer InteractionLockedUntil { get; set; }

		public ItemDefinition Definition =>
			ItemDatabase.Instance != null ? ItemDatabase.Instance.GetById(ItemId) : null;

		private Rigidbody _rb;
		private Collider _col;
		private static readonly Collider[] s_pushBuffer = new Collider[8];

		/// <summary>Authority-only. Call right after Runner.Spawn for programmatic pickups.</summary>
		public void Initialize(short itemId, short count)
		{
			ItemId = itemId;
			Count = count;
		}

		public override void Spawned()
		{
			_rb = GetComponent<Rigidbody>();
			_col = GetComponentInChildren<Collider>();

			if (Object.HasStateAuthority)
			{
				if (ItemId == 0 && _lootTable != null && _lootTable.Count > 0)
				{
					var entry = _randomize ? PickWeighted(_lootTable, transform.position) : _lootTable[0];
					if (entry.Item != null)
					{
						ItemId = entry.Item.Id;
						Count = entry.Count > 0 ? entry.Count : (short)1;
					}
				}

				if (ItemId == 0 && _initialItem != null)
				{
					ItemId = _initialItem.Id;
					Count = _initialCount;
				}
				NetPosition = transform.position;
				NetRotation = transform.rotation;

				// SA also keeps the Rigidbody asleep when scene-placed so it doesn't drift before being thrown.
				if (_rb != null && _rb.linearVelocity == Vector3.zero) _rb.Sleep();
			}
			else if (_rb != null)
			{
				// Remote clients never simulate — physics is SA-only. They follow NetPosition / NetRotation.
				_rb.isKinematic = true;
				_rb.interpolation = RigidbodyInterpolation.None;
			}
		}

		public override void FixedUpdateNetwork()
		{
			// Snapshot the SA's authoritative transform every tick. Fusion interpolates
			// the [Networked] Vector3 / Quaternion automatically when proxies read them
			// in Render, so the visual stays smooth between ticks.
			if (Object.HasStateAuthority && _rb != null)
			{
				ApplyPlayerPush();
				NetPosition = _rb.position;
				NetRotation = _rb.rotation;
			}
		}

		// SimpleKCC is a kinematic capsule, so PhysX never imparts contact velocity to the
		// dynamic pickup Rigidbody. We do it manually: each tick on SA, check for player
		// overlap and shove the item away from the player along the horizontal.
		private void ApplyPlayerPush()
		{
			if (_rb.isKinematic || _col == null) return;

			Vector3 center = _rb.position;
			var ext = _col.bounds.extents;
			float radius = Mathf.Max(ext.x, ext.y, ext.z) + _playerPushRadius;

			int count = Physics.OverlapSphereNonAlloc(center, radius, s_pushBuffer, ~0, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < count; i++)
			{
				var col = s_pushBuffer[i];
				if (col == null) continue;
				if (col.attachedRigidbody == _rb) continue;

				var kcc = col.GetComponentInParent<SimpleKCC>();
				if (kcc == null) continue;

				Vector3 away = center - kcc.transform.position;
				away.y = 0f;
				if (away.sqrMagnitude < 1e-6f)
				{
					// Player standing directly on the item — pick an arbitrary horizontal direction.
					away = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
					if (away.sqrMagnitude < 1e-6f) away = Vector3.right;
				}
				away.Normalize();

				Vector3 playerVel = kcc.RealVelocity; playerVel.y = 0f;
				float intoSpeed = Mathf.Max(Vector3.Dot(playerVel, away), 0f);
				float pushSpeed = Mathf.Min(intoSpeed + _baselinePushSpeed, _maxPushSpeed);

				Vector3 newVel = away * pushSpeed;
				newVel.y = _rb.linearVelocity.y;
				_rb.linearVelocity = newVel;
				if (_rb.IsSleeping()) _rb.WakeUp();
			}
		}

		public override void Render()
		{
			if (Object.HasStateAuthority == false)
			{
				transform.SetPositionAndRotation(NetPosition, NetRotation);
			}
		}

		// --- IKnockbackable ---

		// Combat actions (bat, punch, pistol) push items when they hit. We just convert
		// the KnockbackDistance to an initial impulse on the Rigidbody — friction/damping
		// handle the rest.
		void IKnockbackable.ApplyKnockback(Vector3 fromPosition, float distance)
		{
			if (Object.HasStateAuthority == false) return;
			if (_rb == null || _rb.isKinematic || distance <= 0f) return;

			Vector3 dir = transform.position - fromPosition;
			dir.y = 0f;
			if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;
			dir.Normalize();

			Vector3 vel = dir * (distance * _knockbackImpulseScale);
			vel.y = Mathf.Max(_rb.linearVelocity.y, distance * _knockbackUpScale);
			_rb.linearVelocity = vel;
			if (_rb.IsSleeping()) _rb.WakeUp();
		}

		/// <summary>
		/// SA-only. Snap the pickup to <paramref name="position"/>/<paramref name="rotation"/>,
		/// freeze its Rigidbody (kinematic, zero velocity) and snapshot the networked transform
		/// so it stays planted exactly where a projectile landed. Pickup remains interactable.
		/// </summary>
		public void Stick(Vector3 position, Quaternion rotation)
		{
			if (Object.HasStateAuthority == false) return;
			if (_rb == null) _rb = GetComponent<Rigidbody>();

			transform.SetPositionAndRotation(position, rotation);

			if (_rb != null)
			{
				_rb.linearVelocity = Vector3.zero;
				_rb.angularVelocity = Vector3.zero;
				_rb.isKinematic = true;
				if (_rb.IsSleeping() == false) _rb.Sleep();
			}

			NetPosition = position;
			NetRotation = rotation;
		}

		/// <summary>
		/// SA-only. Apply a one-shot impulse via the local Rigidbody and lock interactions
		/// (so the thrower can't immediately re-grab the item) for <paramref name="lockSeconds"/>.
		/// </summary>
		public void Throw(Vector3 velocity, float lockSeconds = 1f)
		{
			if (Object.HasStateAuthority == false) return;
			if (_rb == null) _rb = GetComponent<Rigidbody>();
			if (_rb != null)
			{
				_rb.isKinematic = false;
				_rb.linearVelocity = velocity;
				_rb.angularVelocity = new Vector3(Random.Range(-2f, 2f), Random.Range(-2f, 2f), Random.Range(-2f, 2f));
			}
			InteractionLockedUntil = TickTimer.CreateFromSeconds(Runner, lockSeconds);
		}

		// --- IInteractable ---

		float IInteractable.InteractRange => PickupRange;
		string IInteractable.InteractLabel => Definition != null ? Definition.DisplayName : "Item";
		bool IInteractable.CanInteract
		{
			get
			{
				if (Object == null || !Object.IsValid) return false;
				if (Runner != null && InteractionLockedUntil.ExpiredOrNotRunning(Runner) == false) return false;
				return true;
			}
		}
		Vector3 IInteractable.InteractionPoint => transform.position;
		string IInteractable.LockedReason => null;

		void IInteractable.OnInteract(InteractionScanner scanner)
		{
			var target = scanner.GetComponent<IPickupTarget>();
			if (target != null) target.OnPickupRequested(this);
		}

		// Same helper signature is duplicated on LootContainer; keeping the body short
		// and inline avoids a new dependency for two callers.
		private static LootEntry PickWeighted(List<LootEntry> entries, Vector3 worldPos)
		{
			float total = 0f;
			for (int i = 0; i < entries.Count; i++)
			{
				if (entries[i].Item == null) continue;
				if (entries[i].Weight > 0f) total += entries[i].Weight;
			}

			if (total <= 0f)
			{
				// All weights zero or missing items — fall back to first non-null.
				for (int i = 0; i < entries.Count; i++)
					if (entries[i].Item != null) return entries[i];
				return default;
			}

			var rng = WorldGen.RngFor(worldPos);
			float pick = (float)(rng.NextDouble() * total);
			float acc = 0f;
			for (int i = 0; i < entries.Count; i++)
			{
				if (entries[i].Item == null || entries[i].Weight <= 0f) continue;
				acc += entries[i].Weight;
				if (pick <= acc) return entries[i];
			}
			return entries[entries.Count - 1];
		}
	}

	/// <summary>
	/// Implemented by the mode-specific player inventory (e.g. <c>Starter.Shooter.Inventory</c>)
	/// so <see cref="PickupableItem"/> can route the actual pickup RPC without taking a
	/// hard dependency on the mode's assembly.
	/// </summary>
	public interface IPickupTarget
	{
		void OnPickupRequested(PickupableItem pickup);
	}
}
