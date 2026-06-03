using System.Collections.Generic;
using Fusion;
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
	public sealed class PickupableItem : PhysicsBody, IInteractable
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

		[Header("Generic visual")]
		[Tooltip("Child whose MeshFilter/MeshRenderer/ItemView is configured from the item's ItemVisual " +
		         "(mesh/material/scale) at spawn. Set on Pickup_Generic; null on bespoke prefabs that bake their own model.")]
		[SerializeField] private Transform _visualRoot;

		[Header("Interaction")]
		[Tooltip("Max distance from the player at which this can be picked up.")]
		public float PickupRange = 2f;

		[Networked, OnChangedRender(nameof(OnItemIdChanged))] public short ItemId { get; set; }
		[Networked] public short Count { get; set; }
		[Networked] public Vector3 NetPosition { get; set; }
		[Networked] public Quaternion NetRotation { get; set; }
		[Networked] public TickTimer InteractionLockedUntil { get; set; }

		// Set when this pickup is lodged in a damageable target (thrown-knife-in-a-body). While valid the
		// pickup follows the victim with physics off and its colliders turned into triggers (still
		// interactable, but can't block or be re-hit); cleared when the victim dies/despawns so it falls.
		[Networked, OnChangedRender(nameof(OnAttachedChanged))] public NetworkId AttachedTo { get; set; }
		[Networked] public Vector3 AttachLocalPos { get; set; }
		[Networked] public Quaternion AttachLocalRot { get; set; }

		public ItemDefinition Definition =>
			ItemDatabase.Instance != null ? ItemDatabase.Instance.GetById(ItemId) : null;

		// A dropped item's float behaviour is a property of the item type, not the (shared) generic pickup
		// prefab, so source it from the ItemDefinition. Falls back to the base prefab flag when the item
		// can't resolve yet (e.g. before ItemId replicates).
		public override bool Floats => Definition != null ? Definition.Floats : _floats;

		// Center of mass is likewise per-item (a low COM keeps a floating item upright), so read it from
		// the ItemDefinition; fall back to the prefab override when the item can't resolve.
		protected override bool TryGetCenterOfMass(out Vector3 com)
		{
			var def = Definition;
			if (def != null && def.OverrideCenterOfMass)
			{
				com = def.CenterOfMass;
				return true;
			}
			return base.TryGetCenterOfMass(out com);
		}

		private bool _isSpawned;
		private GameObject _visualOverrideInstance;

		/// <summary>Authority-only. Call right after Runner.Spawn for programmatic pickups.</summary>
		public void Initialize(short itemId, short count)
		{
			ItemId = itemId;
			Count = count;
		}

		public override void Spawned()
		{
			_isSpawned = true;
			CachePhysicsRefs();

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

			// Build the visual + apply the item's physics tuning now in case ItemId was already set before
			// Spawned (scene-placed / SA). For programmatic spawns that call Initialize() after Spawn,
			// OnItemIdChanged covers both.
			ConfigureVisual();
			ApplyCenterOfMass();
		}

		// ItemId is the single networked input for the visual — every peer builds the same model
		// deterministically from it (no networked visual state). Fires on SA when Initialize/loot sets
		// it, and on proxies when it replicates. It also resolves the ItemDefinition's physics tuning,
		// so re-apply the center of mass here.
		private void OnItemIdChanged()
		{
			ConfigureVisual();
			ApplyCenterOfMass();
		}

		/// <summary>
		/// Configure the generic pickup's Visual child from the item's <see cref="ItemVisual"/>:
		/// mesh/material/scale + ItemView flair, or instantiate <see cref="ItemVisual.VisualOverride"/>.
		/// No-op on bespoke prefabs (those leave <c>_visualRoot</c> null and bake their own visual).
		/// </summary>
		private void ConfigureVisual()
		{
			if (_visualRoot == null) return;

			var def = Definition;
			var v = def != null ? def.Visual : null;
			if (v == null) return;

			if (v.VisualOverride != null)
			{
				if (_visualOverrideInstance == null)
					_visualOverrideInstance = Instantiate(v.VisualOverride, _visualRoot);
				if (_visualRoot.TryGetComponent<MeshRenderer>(out var primRenderer))
					primRenderer.enabled = false;
				_visualRoot.localScale = v.WorldScale;
				return;
			}

			if (v.Mesh != null && _visualRoot.TryGetComponent<MeshFilter>(out var mf))
				mf.sharedMesh = v.Mesh;
			if (_visualRoot.TryGetComponent<MeshRenderer>(out var mr))
			{
				if (v.Material != null) mr.sharedMaterial = v.Material;
				mr.enabled = true;
			}
			_visualRoot.localScale = v.WorldScale;
			if (_visualRoot.TryGetComponent<ItemView>(out var view))
			{
				view.Bob = v.WorldBob;
				view.Rotate = v.WorldRotate;
			}
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			// Fusion pools NetworkObjects — clear the flag so a reused instance can't be
			// treated as spawned before its next Spawned() call.
			_isSpawned = false;
		}

		public override void FixedUpdateNetwork()
		{
			if (Object.HasStateAuthority == false) return;

			// Lodged in a target: follow it (physics is off) until it dies/despawns, then fall.
			if (AttachedTo.IsValid)
			{
				UpdateAttachment();
				return;
			}

			// Snapshot the SA's authoritative transform every tick. Fusion interpolates
			// the [Networked] Vector3 / Quaternion automatically when proxies read them
			// in Render, so the visual stays smooth between ticks.
			if (_rb != null)
			{
				ApplyPlayerPush();
				TickSettle(Runner.DeltaTime);
				NetPosition = _rb.position;
				NetRotation = _rb.rotation;
			}
		}

		/// <summary>
		/// SA-only. Lodge this pickup in a damageable target: it follows the victim with physics
		/// disabled and its colliders turned into triggers, so it can't block movement or be re-hit but
		/// is still grabbable. When the victim dies or despawns it detaches and falls (UpdateAttachment).
		/// Used by Projectile when a thrown weapon hits a Health target.
		/// </summary>
		public void AttachToVictim(NetworkObject victim, Vector3 worldPos, Quaternion worldRot, float lockSeconds = 0.5f)
		{
			if (Object.HasStateAuthority == false || victim == null) return;
			if (_rb == null) _rb = GetComponent<Rigidbody>();

			var vt = victim.transform;
			AttachLocalPos = vt.InverseTransformPoint(worldPos);
			AttachLocalRot = Quaternion.Inverse(vt.rotation) * worldRot;
			AttachedTo = victim.Id; // OnAttachedChanged turns colliders into triggers on every peer

			if (_rb != null)
			{
				_rb.linearVelocity = Vector3.zero;
				_rb.angularVelocity = Vector3.zero;
				_rb.isKinematic = true;
				if (_rb.IsSleeping() == false) _rb.Sleep();
			}

			NetPosition = worldPos;
			NetRotation = worldRot;
			InteractionLockedUntil = TickTimer.CreateFromSeconds(Runner, lockSeconds);
		}

		// SA-only. Follow the victim while it's alive; detach + fall once it's dead or gone.
		private void UpdateAttachment()
		{
			if (Runner.TryFindObject(AttachedTo, out var victim) && victim != null)
			{
				var health = victim.GetComponentInChildren<Health>();
				if (health == null || health.IsAlive)
				{
					var vt = victim.transform;
					NetPosition = vt.TransformPoint(AttachLocalPos);
					NetRotation = vt.rotation * AttachLocalRot;
					return;
				}
			}
			Detach();
		}

		// SA-only. Release from the victim: physics back on with a small tumble so it falls off the body.
		private void Detach()
		{
			AttachedTo = default; // OnAttachedChanged restores solid colliders on every peer
			if (_rb != null)
			{
				_rb.isKinematic = false;
				_rb.linearVelocity = Vector3.zero;
				_rb.angularVelocity = new Vector3(Random.Range(-2f, 2f), Random.Range(-2f, 2f), Random.Range(-2f, 2f));
				_rb.WakeUp();
			}
		}

		// Runs on every peer when AttachedTo changes: a lodged pickup's colliders become triggers (no
		// physics collision, can't be re-hit, but the interaction scan's OverlapSphere still finds them);
		// a detached pickup gets solid colliders back so it lands on the ground.
		private void OnAttachedChanged()
		{
			bool attached = AttachedTo.IsValid;
			foreach (var c in GetComponentsInChildren<Collider>(true))
				c.isTrigger = attached;
		}

		public override void Render()
		{
			if (Object.HasStateAuthority == false)
			{
				transform.SetPositionAndRotation(NetPosition, NetRotation);
			}
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
				// _isSpawned guards the [Networked] access below: the local InteractionScanner can
				// pick up this collider via OverlapSphere the frame it's instantiated, before Fusion
				// has run Spawned() — reading a networked property in that window throws.
				if (!_isSpawned || Object == null || !Object.IsValid) return false;
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
