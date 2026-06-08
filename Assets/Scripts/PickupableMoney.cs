using Fusion;
using Starter.Common.Interactions;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Networked world pickup that grants the local player <see cref="Player.Money"/>
	/// on tap-Interact. Implements <see cref="IInteractable"/> so it shows up in the
	/// standard interaction prompt and routes through <see cref="InteractionScanner"/>
	/// like every other interactable in the project.
	///
	/// Spawn it via <c>Runner.Spawn</c> from loot drops, sell prompts, quest rewards,
	/// or place it directly in the scene. Programmatic spawns can override the granted
	/// amount via <see cref="Initialize"/>.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	[RequireComponent(typeof(InteractionPrompt))]
	public sealed class PickupableMoney : NetworkBehaviour, IInteractable
	{
		[Header("Authoring")]
		[Tooltip("Money granted on pickup. Scene-placed pickups use this directly; programmatic spawns can override via Initialize().")]
		[Min(1)] public int Amount = 1;

		[Tooltip("Display label used in interaction toasts when the pickup is locked. Kept for future hooks (e.g. \"out of inventory space\" — money has none).")]
		public string DisplayName = "Money";

		[Tooltip("Max distance from the player at which Interact will pick this up. The host re-validates with a 1.25x slack.")]
		[Min(0.1f)] public float InteractRange = 2f;

		[Tooltip("Seconds the pickup is uninteractable after spawn — gives a Throw arc time to leave the spawner before it can be grabbed back.")]
		[Min(0f)] public float SpawnGraceSeconds = 0.5f;

		[Networked] public int NetAmount { get; set; }
		[Networked] public Vector3 NetPosition { get; set; }
		[Networked] public Quaternion NetRotation { get; set; }
		[Networked] private TickTimer _spawnGrace { get; set; }

		private Rigidbody _rb;

		/// <summary>Authority-only. Call right after <c>Runner.Spawn</c> for programmatic pickups to override the authored amount.</summary>
		public void Initialize(int amount)
		{
			if (HasStateAuthority == false) return;
			NetAmount = Mathf.Max(1, amount);
		}

		public override void Spawned()
		{
			_rb = GetComponent<Rigidbody>();

			if (HasStateAuthority)
			{
				if (NetAmount <= 0) NetAmount = Mathf.Max(1, Amount);
				NetPosition = transform.position;
				NetRotation = transform.rotation;
				_spawnGrace = TickTimer.CreateFromSeconds(Runner, SpawnGraceSeconds);

				if (_rb != null && _rb.linearVelocity == Vector3.zero) _rb.Sleep();
			}
			else if (_rb != null)
			{
				_rb.isKinematic = true;
				_rb.interpolation = RigidbodyInterpolation.None;
			}
		}

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false) return;
			if (_rb == null || _rb.isKinematic) return;

			NetPosition = _rb.position;
			NetRotation = _rb.rotation;
		}

		public override void Render()
		{
			if (HasStateAuthority == false)
			{
				transform.SetPositionAndRotation(NetPosition, NetRotation);
			}
		}

		// --- IInteractable ---

		float IInteractable.InteractRange => InteractRange;
		Vector3 IInteractable.InteractionPoint => transform.position;
		bool IInteractable.CanInteract
		{
			get
			{
				if (Object == null || Object.IsValid == false) return false;
				if (Runner != null && _spawnGrace.ExpiredOrNotRunning(Runner) == false) return false;
				return true;
			}
		}
		string IInteractable.LockedReason => null;

		void IInteractable.OnInteract(InteractionScanner scanner)
		{
			RPC_RequestPickup();
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_RequestPickup(RpcInfo info = default)
		{
			if (Runner == null) return;
			if (_spawnGrace.ExpiredOrNotRunning(Runner) == false) return;

			var source = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
			var playerObj = Runner.GetPlayerObject(source);
			if (playerObj == null) return;

			float allowed = InteractRange * 1.25f;
			if ((playerObj.transform.position - transform.position).sqrMagnitude > allowed * allowed) return;

			var player = playerObj.GetComponent<Player>();
			if (player == null) return;
			if (player.Health == null || player.Health.IsAlive == false) return;

			player.AddMoney(NetAmount);
			Runner.Despawn(Object);
		}

		/// <summary>SA-only one-shot impulse via the local Rigidbody (used by loot/sell spawners that want a toss arc).</summary>
		public void Throw(Vector3 velocity)
		{
			if (HasStateAuthority == false) return;
			if (_rb == null) _rb = GetComponent<Rigidbody>();
			if (_rb == null) return;

			_rb.isKinematic = false;
			_rb.linearVelocity = velocity;
			_rb.angularVelocity = new Vector3(Random.Range(-2f, 2f), Random.Range(-2f, 2f), Random.Range(-2f, 2f));
			if (_rb.IsSleeping()) _rb.WakeUp();
		}
	}
}
