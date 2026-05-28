using Fusion;
using Starter.Common.Interactions;
using UnityEngine;

namespace Starter.Shooter
{
	public enum ESeatRole : byte
	{
		Passenger = 0,
		Driver = 1,
	}

	/// <summary>
	/// One seat on a vehicle. Implements <see cref="IInteractable"/> so the existing
	/// player scanner can be used to enter. Holds the occupant's <see cref="PlayerRef"/>
	/// in networked state so every peer can render the player parked on the anchor.
	///
	/// Designed as a self-contained drop-in prefab: bring its own NetworkObject so you
	/// can nest multiple seats under any Vehicle and have them auto-register via
	/// GetComponentInParent on Spawned.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	public sealed class Seat : NetworkBehaviour, IInteractable
	{
		[Header("Authoring")]
		public ESeatRole Role = ESeatRole.Passenger;
		[Min(0f)] public float InteractRangeValue = 2.5f;

		[Tooltip("Where the seated player's root is snapped to. If null, the seat's own transform is used.")]
		public Transform SeatAnchor;

		[Tooltip("Where the player is teleported when they leave the seat. If null, a point 1.5m to the seat's right is used.")]
		public Transform ExitPoint;

		[Networked, OnChangedRender(nameof(OnOccupantChanged))]
		public PlayerRef Occupant { get; set; }

		public Vehicle Vehicle { get; private set; }
		public Transform Anchor => SeatAnchor != null ? SeatAnchor : transform;

		public override void Spawned()
		{
			Vehicle = GetComponentInParent<Vehicle>();
			if (Vehicle != null) Vehicle.RegisterSeat(this);
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (Vehicle != null) Vehicle.UnregisterSeat(this);
		}

		// --- IInteractable ---

		float IInteractable.InteractRange => InteractRangeValue;
		bool IInteractable.CanInteract => Occupant == PlayerRef.None;
		Vector3 IInteractable.InteractionPoint => transform.position;
		string IInteractable.LockedReason => "Seat occupied";

		void IInteractable.OnInteract(InteractionScanner scanner)
		{
			RPC_RequestEnter();
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_RequestEnter(RpcInfo info = default)
		{
			var source = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;

			if (Occupant != PlayerRef.None) return;

			var playerObj = Runner.GetPlayerObject(source);
			if (playerObj == null) return;

			float allowed = InteractRangeValue * 1.25f;
			float distSq = (playerObj.transform.position - transform.position).sqrMagnitude;
			if (distSq > allowed * allowed) return;

			var player = playerObj.GetComponent<Player>();
			if (player == null) return;
			if (player.InCurrentSeat != null) return;

			Occupant = source;
			player.HostEnterSeat(this);

			if (Role == ESeatRole.Driver && Vehicle != null)
			{
				Vehicle.Object.AssignInputAuthority(source);
			}
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_RequestExit(RpcInfo info = default)
		{
			var source = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
			if (Occupant != source) return;
			ReleaseOccupant();
		}

		/// <summary>State-authority-only force release (player disconnected, vehicle despawned, etc.).</summary>
		public void HostForceRelease()
		{
			if (Object == null || Object.HasStateAuthority == false) return;
			if (Occupant == PlayerRef.None) return;
			ReleaseOccupant();
		}

		private void ReleaseOccupant()
		{
			var who = Occupant;
			var playerObj = Runner.GetPlayerObject(who);
			Vector3 exit = ResolveSafeExitPosition();

			if (playerObj != null)
			{
				var player = playerObj.GetComponent<Player>();
				if (player != null)
				{
					player.HostExitSeat(exit);
				}
			}

			Occupant = PlayerRef.None;

			if (Role == ESeatRole.Driver && Vehicle != null)
			{
				Vehicle.Object.RemoveInputAuthority();
			}
		}

		// Player capsule dimensions used to validate the exit slot. Match Player.KCC authoring.
		private const float ExitCapsuleRadius = 0.4f;
		private const float ExitCapsuleHeight = 1.8f;
		private static readonly Collider[] _exitOverlapBuf = new Collider[16];

		/// <summary>
		/// Picks an exit position that's guaranteed not to be inside this vehicle's colliders.
		/// Starts from the authored <see cref="ExitPoint"/> and, if the player capsule would
		/// clip the vehicle there, pushes outward along the (anchor → exit) direction in 0.25 m
		/// steps until a clear slot is found (or we give up and fall back to the authored point).
		/// Important when the vehicle is tilted/rotated and the authored offset rotates with it.
		/// </summary>
		private Vector3 ResolveSafeExitPosition()
		{
			Vector3 anchor = transform.position;
			Vector3 baseExit = ExitPoint != null
				? ExitPoint.position
				: anchor + transform.right * 1.5f;

			if (Vehicle == null) return baseExit;

			Vector3 dir = baseExit - anchor;
			float dist = dir.magnitude;
			if (dist < 0.01f)
			{
				dir = transform.right;
			}
			else
			{
				dir /= dist;
			}
			// Keep the push horizontal so we don't shove the player up into the air or down through the ground.
			dir.y = 0f;
			if (dir.sqrMagnitude < 0.0001f) dir = Vector3.right;
			else dir.Normalize();

			if (!OverlapsThisVehicle(baseExit)) return baseExit;

			for (float push = 0.25f; push <= 5f; push += 0.25f)
			{
				Vector3 trial = baseExit + dir * push;
				if (!OverlapsThisVehicle(trial)) return trial;
			}

			return baseExit;
		}

		private bool OverlapsThisVehicle(Vector3 feetPosition)
		{
			Vector3 bottom = feetPosition + Vector3.up * ExitCapsuleRadius;
			Vector3 top = feetPosition + Vector3.up * (ExitCapsuleHeight - ExitCapsuleRadius);
			int count = Physics.OverlapCapsuleNonAlloc(bottom, top, ExitCapsuleRadius, _exitOverlapBuf, ~0, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < count; i++)
			{
				var col = _exitOverlapBuf[i];
				if (col == null) continue;
				if (col.GetComponentInParent<Vehicle>() == Vehicle) return true;
			}
			return false;
		}

		private void OnOccupantChanged()
		{
			// Hook for future SFX (door open/close) — keep silent for v1.
		}

		private void OnDrawGizmosSelected()
		{
			Gizmos.color = Role == ESeatRole.Driver ? new Color(0.2f, 0.8f, 0.2f, 0.8f) : new Color(0.8f, 0.8f, 0.2f, 0.8f);
			var a = Anchor;
			Gizmos.DrawWireSphere(a.position, 0.25f);
			Gizmos.DrawLine(a.position, a.position + a.forward * 0.5f);
			if (ExitPoint != null)
			{
				Gizmos.color = new Color(0.8f, 0.3f, 0.3f, 0.8f);
				Gizmos.DrawWireSphere(ExitPoint.position, 0.2f);
			}
		}
	}
}
