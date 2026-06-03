using Fusion;
using Fusion.Addons.SimpleKCC;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Generic networked dynamic physics object. Drop this on any prop (barrel, crate, can, sign)
	/// and it reacts to every force in the game — shooting, melee, explosions, vehicle impacts,
	/// and players walking into it.
	///
	/// No Fusion physics addon in this project, so it follows the same host-authoritative model as
	/// <see cref="Vehicle"/>: the host owns a dynamic <see cref="Rigidbody"/>, proxies are kinematic
	/// (so the <see cref="NetworkTransform"/>'s writes aren't fought by local PhysX), and a
	/// <see cref="NetworkTransform"/> replicates the resulting pose. World/vehicle collisions only
	/// resolve on the host; clients see the interpolated result.
	///
	/// Combat reacts to it for free: <see cref="HitscanAction"/>, <see cref="OverlapAction"/> and
	/// <see cref="GrenadeProjectile"/> all already call <see cref="IKnockbackable.ApplyKnockback"/>
	/// on any hit collider that has no <see cref="Health"/>. Driving into it works for free too —
	/// the vehicle and this prop share the host's PhysX scene, so a heavier <c>Mass</c> resists the
	/// shove while a light one is sent flying. Only "walk into it" needs manual code, because
	/// <see cref="SimpleKCC"/> is kinematic and never imparts contact velocity.
	///
	/// All forces are applied as <see cref="ForceMode.Impulse"/> so the Rigidbody's <c>Mass</c> is
	/// meaningful across every source: heavy props shrug off a pistol shot; light ones scatter.
	///
	/// Put the prop on the Default layer so combat queries (which mask the Default layer) find it.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	[RequireComponent(typeof(Rigidbody))]
	[RequireComponent(typeof(NetworkTransform))]
	public sealed class PhysicsProp : NetworkBehaviour, IKnockbackable, IBuoyantBody
	{
		[Header("Water")]
		[Tooltip("ON: this prop floats to the surface of any WaterVolume it's dropped in and bobs there. " +
		         "OFF (default): it sinks. See WaterVolume / IBuoyantBody.")]
		[SerializeField] private bool _floats = false;

		[Header("Hit Response (shoot / melee / explosion)")]
		[Tooltip("Multiplier converting a CombatAction's KnockbackDistance (meters) into a horizontal impulse. " +
		         "Because it's an impulse, a heavier Mass produces less velocity — tune this against your prop masses.")]
		[SerializeField] private float _knockbackImpulseScale = 6f;
		[Tooltip("Upward impulse added per unit of KnockbackDistance, for a small toss/scatter arc.")]
		[SerializeField] private float _knockbackUpScale = 3f;

		[Header("Player Push (walk into it — intentionally weaker)")]
		[Tooltip("Extra radius (m) beyond the prop's collider bounds used to detect player overlap. SimpleKCC is " +
		         "kinematic, so PhysX never pushes the prop on contact — this manual sweep is what makes it move.")]
		[SerializeField] private float _playerPushRadius = 0.2f;
		[Tooltip("Baseline impulse strength when a stationary player just leans on the prop.")]
		[SerializeField] private float _baselinePushImpulse = 0.6f;
		[Tooltip("Impulse gained per (m/s) of the player's speed moving INTO the prop. Lets a running shove move it more.")]
		[SerializeField] private float _pushSpeedScale = 0.8f;
		[Tooltip("Upper bound on the walk-into impulse so a sprinting player can't punt props across the map.")]
		[SerializeField] private float _maxPushImpulse = 5f;

		private Rigidbody _rb;
		private Collider _col;
		private static readonly Collider[] s_pushBuffer = new Collider[8];

		public override void Spawned()
		{
			_rb = GetComponent<Rigidbody>();
			_col = GetComponentInChildren<Collider>();

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
			// Pose is replicated by NetworkTransform; nothing else to write here.
		}

		// --- IBuoyantBody: WaterVolume reads these to apply buoyancy on the host ---

		Rigidbody IBuoyantBody.Body => _rb;
		bool IBuoyantBody.Floats => _floats;

		// --- IKnockbackable: shooting, melee and explosions all route here ---

		// fromPosition is the push origin (attacker position for melee/hitscan, blast centre for a grenade);
		// distance is the action's KnockbackDistance, already scaled by charge / explosion falloff.
		void IKnockbackable.ApplyKnockback(Vector3 fromPosition, float distance)
		{
			if (Object.HasStateAuthority == false) return;
			if (_rb == null || _rb.isKinematic || distance <= 0f) return;

			Vector3 dir = transform.position - fromPosition;
			dir.y = 0f;
			if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;
			dir.Normalize();

			Vector3 impulse = dir * (distance * _knockbackImpulseScale);
			impulse.y += distance * _knockbackUpScale;

			if (_rb.IsSleeping()) _rb.WakeUp();
			_rb.AddForce(impulse, ForceMode.Impulse);
		}

		// --- Walk-into push (SimpleKCC is kinematic, so do it manually — weaker than a hit) ---

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
					// Player standing directly on the prop — pick an arbitrary horizontal direction.
					away = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
					if (away.sqrMagnitude < 1e-6f) away = Vector3.right;
				}
				away.Normalize();

				Vector3 playerVel = kcc.RealVelocity; playerVel.y = 0f;
				float intoSpeed = Mathf.Max(Vector3.Dot(playerVel, away), 0f);
				float impulse = Mathf.Min(_baselinePushImpulse + intoSpeed * _pushSpeedScale, _maxPushImpulse);

				if (_rb.IsSleeping()) _rb.WakeUp();
				_rb.AddForce(away * impulse, ForceMode.Impulse);
			}
		}
	}
}
