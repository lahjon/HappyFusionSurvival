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
			Debug.Log($"[Seat:{name}] OnInteract called by scanner on '{scanner?.name}'. Sending RPC_RequestEnter.");
			RPC_RequestEnter();
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_RequestEnter(RpcInfo info = default)
		{
			var source = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
			Debug.Log($"[Seat:{name}] RPC_RequestEnter from {source} (Occupant={Occupant}, Role={Role})");

			if (Occupant != PlayerRef.None) { Debug.Log("  rejected: seat occupied"); return; }

			var playerObj = Runner.GetPlayerObject(source);
			if (playerObj == null) { Debug.Log("  rejected: no player object"); return; }

			float allowed = InteractRangeValue * 1.25f;
			float distSq = (playerObj.transform.position - transform.position).sqrMagnitude;
			if (distSq > allowed * allowed) { Debug.Log($"  rejected: out of range dsq={distSq:F2} allowedSq={allowed * allowed:F2}"); return; }

			var player = playerObj.GetComponent<Player>();
			if (player == null) { Debug.Log("  rejected: no Player component"); return; }
			if (player.InCurrentSeat != null) { Debug.Log($"  rejected: player already seated in {player.InCurrentSeat.name}"); return; }

			Occupant = source;
			player.HostEnterSeat(Object);

			if (Role == ESeatRole.Driver && Vehicle != null)
			{
				Vehicle.Object.AssignInputAuthority(source);
				Debug.Log($"  driver entered — vehicle input authority transferred to {source}");
			}
			else
			{
				Debug.Log("  passenger entered");
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
			Vector3 exit = ExitPoint != null
				? ExitPoint.position
				: transform.position + transform.right * 1.5f;

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
