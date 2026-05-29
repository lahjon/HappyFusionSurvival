using Fusion;
using Starter.Common.Interactions;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Networked bed station. Single occupant per bed; sleeping is only allowed at night.
	/// Body-pose stays where it is — the per-player local <see cref="SleepSession"/> owns the
	/// camera lerp toward <see cref="SleepViewTransform"/>, mirroring the <see cref="Computer"/>
	/// pattern. <see cref="GameManager"/> watches all players' <see cref="Player.IsSleeping"/>
	/// flags and force-wakes every bed when everyone is asleep.
	/// </summary>
	public sealed class Bed : InteractableStation
	{
		[Header("Sleep")]
		[Tooltip("Pose the local camera lerps to while sleeping in this bed. Author it on the prefab — typically just above the pillow, angled to look up at the sky.")]
		public Transform SleepViewTransform;

		[Tooltip("Seconds to ease into / out of the sleep view.")]
		[Min(0f)] public float ZoomDuration = 0.6f;

		[Tooltip("Easing applied to the 0..1 lerp parameter during zoom in/out. Default smoothstep-ish.")]
		public AnimationCurve ZoomEase = new AnimationCurve(
			new Keyframe(0f, 0f, 0f, 0f),
			new Keyframe(1f, 1f, 0f, 0f));

		/// <summary>The player currently sleeping in this bed, or <see cref="PlayerRef.None"/> when free. State-authority writes only.</summary>
		[Networked, OnChangedRender(nameof(OnOccupantChanged))]
		public PlayerRef Occupant { get; private set; }

		public bool HasOccupant => Occupant != PlayerRef.None;
		public bool IsLocalOccupant => Runner != null && HasOccupant && Occupant == Runner.LocalPlayer;

		public override bool CanInteract
		{
			get
			{
				if (HasOccupant) return false;
				var time = TimeManager.Instance;
				return time == null || time.IsNight;
			}
		}

		public override string LockedReason
		{
			get
			{
				if (HasOccupant) return "Occupied";
				var time = TimeManager.Instance;
				if (time != null && time.IsNight == false) return "Sleep only at night";
				return string.Empty;
			}
		}

		/// <summary>Block pickup while someone is sleeping in the bed — yanking the bed out from under them would strand the camera.</summary>
		public override string PickupBlockedReason => HasOccupant ? "Someone is sleeping here" : string.Empty;

		protected override void OnInteract(InteractionScanner scanner)
		{
			if (SleepViewTransform == null)
			{
				Debug.LogWarning($"[Bed:{name}] SleepViewTransform not assigned — cannot sleep.", this);
				return;
			}

			var session = scanner.GetComponent<SleepSession>();
			if (session != null) session.TryOpen(this);
		}

		/// <summary>Called locally by <see cref="SleepSession.TryOpen"/> to ask the host for occupancy.</summary>
		public void LocalRequestSleep() => RPC_RequestSleep();

		/// <summary>Called locally by <see cref="SleepSession.RequestWake"/> to release the bed.</summary>
		public void LocalRequestWake() => RPC_RequestWake();

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_RequestSleep(RpcInfo info = default)
		{
			if (Runner == null) return;
			if (HasOccupant) return;

			var time = TimeManager.Instance;
			if (time == null || time.IsNight == false) return;

			var source = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
			var playerObj = Runner.GetPlayerObject(source);
			if (playerObj == null) return;

			if (!IsWithinHostRange(playerObj.transform.position)) return;

			var player = playerObj.GetComponent<Player>();
			if (player == null || player.IsSleeping) return;

			Occupant = source;
			player.AuthoritySetSleeping(true);
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_RequestWake(RpcInfo info = default)
		{
			if (!HasOccupant) return;

			var source = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
			if (source != Occupant) return; // Only the occupant can choose to wake.

			HostReleaseOccupant();
		}

		/// <summary>State-authority-only: clear the occupant and wake them. Called by <see cref="RPC_RequestWake"/> and by <see cref="GameManager"/> on morning auto-wake.</summary>
		public void HostReleaseOccupant()
		{
			if (HasStateAuthority == false) return;
			if (!HasOccupant) return;

			var playerObj = Runner.GetPlayerObject(Occupant);
			if (playerObj != null)
			{
				var player = playerObj.GetComponent<Player>();
				if (player != null) player.AuthoritySetSleeping(false);
			}

			Occupant = PlayerRef.None;
		}

		private void OnOccupantChanged()
		{
			// Hook for future per-bed VFX (e.g. a sleeping silhouette for remote peers).
		}
	}
}
