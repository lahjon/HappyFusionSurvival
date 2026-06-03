using Fusion;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Generic networked dynamic physics object. Drop this on any prop (barrel, crate, can, sign)
	/// and it reacts to every force in the game — shooting, melee, explosions, vehicle impacts,
	/// and players walking into it.
	///
	/// All the force handling (combat knockback, buoyancy, walk-into push) lives in the shared
	/// <see cref="PhysicsBody"/> base — this class only adds the host-authoritative replication setup.
	/// No Fusion physics addon in this project, so it follows the same model as <see cref="Vehicle"/>:
	/// the host owns a dynamic <see cref="Rigidbody"/>, proxies are kinematic (so the
	/// <see cref="NetworkTransform"/>'s writes aren't fought by local PhysX), and a
	/// <see cref="NetworkTransform"/> replicates the resulting pose.
	///
	/// Combat reacts to it for free: <see cref="HitscanAction"/>, <see cref="OverlapAction"/> and
	/// <see cref="GrenadeProjectile"/> all already call <see cref="IKnockbackable.ApplyKnockback"/>
	/// on any hit collider that has no <see cref="Health"/>.
	///
	/// Put the prop on the Default layer so combat queries (which mask the Default layer) find it.
	/// </summary>
	[RequireComponent(typeof(NetworkTransform))]
	public sealed class PhysicsProp : PhysicsBody
	{
		public override void Spawned()
		{
			CachePhysicsRefs();

			if (Object.HasStateAuthority)
			{
				// Host is the only peer that simulates. Interpolate so the host's own view is smooth
				// between fixed ticks (mirrors Vehicle); the NetworkTransform handles proxies.
				_rb.isKinematic = false;
				_rb.interpolation = RigidbodyInterpolation.Interpolate;
			}
			else
			{
				// Proxies follow the NetworkTransform — kinematic so local PhysX doesn't fight the writes.
				_rb.isKinematic = true;
				_rb.interpolation = RigidbodyInterpolation.None;
			}
		}

		public override void FixedUpdateNetwork()
		{
			if (Object.HasStateAuthority == false) return;
			ApplyPlayerPush();
			TickSettle(Runner.DeltaTime);
			// Pose is replicated by NetworkTransform; nothing else to write here.
		}
	}
}
