using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Reusable "shatters and is gone" component. Drop on any <see cref="NetworkObject"/> prop to make
	/// it break — on a hard physical impact (a thrown pot hitting the ground), when its optional
	/// <see cref="Health"/> is depleted (shot / melee'd), or via an explicit <see cref="Break"/> call.
	/// Breaking drops loot (sibling <see cref="LootDropper"/>, if present), plays a one-shot break
	/// VFX/sound on every peer, then despawns the object.
	///
	/// Host-authoritative, matching <see cref="PhysicsProp"/> / <see cref="Starter.Common.Inventory.PickupableItem"/>:
	/// only the state authority simulates the Rigidbody, so OnCollisionEnter and the break decision run on
	/// the host. The break visual is replicated to every peer with the canonical networked-counter +
	/// OnChangedRender pattern (not an RPC), so it survives the impending despawn — the object is hidden
	/// immediately on every peer and actually despawned a few ticks later.
	///
	/// Pairs naturally with <see cref="Health"/> for the shoot-to-break path: Breakable forces
	/// <c>SuppressDeathVisualSwap</c> so the prop doesn't need VisualRoot/DeathRoot wired, and reads
	/// <c>Health.IsAlive</c> to trigger the break when HP runs out.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	public sealed class Breakable : NetworkBehaviour
	{
		[Header("Impact Break (thrown / knocked into something)")]
		[Tooltip("ON: shatter when hit hard enough by a collision (a thrown pot landing). " +
		         "OFF: only Health depletion / Break() shatters it.")]
		[SerializeField] private bool _breakOnImpact = true;
		[Tooltip("Minimum collision relative speed (m/s) that shatters this. Tune above walking speed so a " +
		         "gentle bump doesn't break it.")]
		[SerializeField] private float _impactBreakSpeed = 4f;
		[Tooltip("The prop must first reach this speed (m/s) before an impact can break it, so a scene-placed " +
		         "prop won't shatter from being nudged — it only arms once actually thrown/launched.")]
		[SerializeField] private float _armSpeed = 3f;
		[Tooltip("Grace period (s) after the prop arms before an impact can break it, so a freshly thrown prop " +
		         "doesn't shatter on the thrower it spawned next to.")]
		[SerializeField] private float _armGrace = 0.15f;

		[Header("On Break")]
		[Tooltip("Optional local-only VFX prefab instantiated at the break position on every peer (e.g. shards + " +
		         "dust). Use a self-destroying prefab.")]
		[SerializeField] private GameObject _breakVfx;
		[Tooltip("Optional sound played at the break position on every peer.")]
		[SerializeField] private AudioClip _breakSound;
		[Range(0f, 1f)]
		[SerializeField] private float _breakVolume = 1f;
		[Tooltip("Seconds between the break (hide + FX on all peers) and the actual despawn. A small delay lets " +
		         "the replicated break visual reach every peer before the object is gone.")]
		[SerializeField] private float _despawnDelay = 0.2f;

		// Bumped by the host when the prop breaks; OnChangedRender fires the break visual on every peer.
		// Counter + OnChangedRender (not RPC) is the canonical one-shot-visual pattern and, unlike an RPC,
		// is not lost to the despawn that follows shortly after.
		[Networked, OnChangedRender(nameof(OnBreakReplicated))] private int _breakCount { get; set; }
		[Networked] private TickTimer _despawnTimer { get; set; }

		private Health _health;
		private Rigidbody _rb;
		private LootDropper _loot;
		private bool _armed;          // SA-only: impact break enabled once moving fast enough
		private TickTimer _armedAt;   // SA-only: grace window after arming
		private bool _broken;         // SA-only reentry guard

		public override void Spawned()
		{
			_health = GetComponent<Health>();
			_rb = GetComponent<Rigidbody>();
			_loot = GetComponent<LootDropper>();
			_armed = false;
			_broken = false;

			// The prop despawns the instant it dies, so its Health never needs a death-visual swap — skip it
			// so the prop doesn't need VisualRoot/DeathRoot wired (Health.Render would NRE otherwise).
			if (_health != null)
				_health.SuppressDeathVisualSwap = true;
		}

		public override void FixedUpdateNetwork()
		{
			if (Object.HasStateAuthority == false) return;

			if (_broken)
			{
				if (_despawnTimer.Expired(Runner))
					Runner.Despawn(Object);
				return;
			}

			// Arm the impact break once the prop is actually moving fast (thrown / knocked), so a resting
			// scene prop can't be shattered by a slow bump. Start the grace window when it first arms.
			if (_breakOnImpact && _armed == false && _rb != null && _rb.isKinematic == false &&
			    _rb.linearVelocity.sqrMagnitude >= _armSpeed * _armSpeed)
			{
				_armed = true;
				_armedAt = TickTimer.CreateFromSeconds(Runner, _armGrace);
			}

			// Shot / melee'd to death (optional Health).
			if (_health != null && _health.IsAlive == false)
				Break();
		}

		private void OnCollisionEnter(Collision collision)
		{
			// Physics only simulates on the host; ignore proxy contacts and post-break contacts.
			if (Object == null || Object.HasStateAuthority == false || _broken) return;
			if (_breakOnImpact == false || _armed == false) return;
			if (_armedAt.ExpiredOrNotRunning(Runner) == false) return; // still in post-throw grace
			if (collision.relativeVelocity.sqrMagnitude >= _impactBreakSpeed * _impactBreakSpeed)
				Break();
		}

		/// <summary>
		/// State-authority only. Drop loot (if a <see cref="LootDropper"/> is present), replicate the break
		/// visual to every peer, and schedule the despawn. Idempotent — safe to call from anywhere on the host.
		/// </summary>
		public void Break()
		{
			if (Object == null || Object.HasStateAuthority == false || _broken) return;
			_broken = true;

			_loot?.DropLoot();

			// Freeze the Rigidbody so it doesn't keep sliding during the brief hide-before-despawn window.
			if (_rb != null && _rb.isKinematic == false)
			{
				_rb.linearVelocity = Vector3.zero;
				_rb.angularVelocity = Vector3.zero;
			}

			_breakCount++; // fires OnBreakReplicated on every peer (FX + hide)
			_despawnTimer = TickTimer.CreateFromSeconds(Runner, _despawnDelay);
		}

		// Runs on every peer when the host bumps _breakCount: play the one-shot break FX and immediately
		// hide the prop (renderers + colliders off) so it reads as shattered right away, ahead of the despawn.
		private void OnBreakReplicated()
		{
			Vector3 position = transform.position;

			if (_breakVfx != null)
				Instantiate(_breakVfx, position, Quaternion.identity);
			if (_breakSound != null)
				AudioSource.PlayClipAtPoint(_breakSound, position, _breakVolume);

			foreach (var r in GetComponentsInChildren<Renderer>(true))
				r.enabled = false;
			foreach (var c in GetComponentsInChildren<Collider>(true))
				c.enabled = false;
		}
	}
}
