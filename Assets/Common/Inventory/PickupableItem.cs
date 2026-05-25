using Fusion;
using Starter.Common.Interactions;
using UnityEngine;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// Networked world object representing a single dropped/lootable stack.
	/// State authority owns the optional Rigidbody simulation (throw arcs after drop);
	/// remote clients read the networked position so they see the same arc.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	public sealed class PickupableItem : NetworkBehaviour, IInteractable
	{
		[Header("Authoring (scene-placed pickups)")]
		[Tooltip("Used when a pickup is placed in the scene. Programmatic spawns call Initialize() instead.")]
		[SerializeField] private ItemDefinition _initialItem;
		[SerializeField, Min(1)] private short _initialCount = 1;

		[Header("Interaction")]
		[Tooltip("Max distance from the player at which this can be picked up.")]
		public float PickupRange = 2f;

		[Networked] public short ItemId { get; set; }
		[Networked] public short Count { get; set; }
		[Networked] public Vector3 NetPosition { get; set; }
		[Networked] public Quaternion NetRotation { get; set; }
		[Networked] public TickTimer InteractionLockedUntil { get; set; }

		public ItemDefinition Definition =>
			ItemDatabase.Instance != null ? ItemDatabase.Instance.GetById(ItemId) : null;

		private Rigidbody _rb;

		/// <summary>Authority-only. Call right after Runner.Spawn for programmatic pickups.</summary>
		public void Initialize(short itemId, short count)
		{
			ItemId = itemId;
			Count = count;
		}

		public override void Spawned()
		{
			_rb = GetComponent<Rigidbody>();

			if (Object.HasStateAuthority)
			{
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
				NetPosition = _rb.position;
				NetRotation = _rb.rotation;
			}
		}

		public override void Render()
		{
			if (Object.HasStateAuthority == false)
			{
				transform.SetPositionAndRotation(NetPosition, NetRotation);
			}
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
