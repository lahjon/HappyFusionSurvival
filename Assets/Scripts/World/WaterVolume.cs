using System.Collections.Generic;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// A region of water. Put this on a GameObject with a <b>trigger</b> collider (e.g. the water box
	/// inside a bathtub, a pond, a pool). Any <see cref="IBuoyantBody"/> whose collider enters the
	/// trigger gets buoyancy and water drag applied every physics step:
	///
	/// <list type="bullet">
	/// <item><b>Floating</b> bodies (<see cref="IBuoyantBody.Floats"/> == true, e.g. the rubber duck)
	/// get an upward acceleration stronger than gravity, so they rise and settle bobbing at the surface.</item>
	/// <item><b>Sinking</b> bodies (the default) get a weaker upward push plus drag, so they sink slowly
	/// and realistically instead of plummeting.</item>
	/// </list>
	///
	/// <para>Networking: this is a plain <see cref="MonoBehaviour"/>, not a NetworkBehaviour, and it
	/// owns no state. The buoyant objects are host-authoritative (host simulates a dynamic Rigidbody,
	/// proxies are kinematic and follow the replicated transform). So WaterVolume only ever does work
	/// on the host — proxies have <c>isKinematic == true</c> rigidbodies, which it skips. The host's
	/// floating result replicates for free via each body's existing NetworkTransform / NetPosition.</para>
	///
	/// Forces use <see cref="ForceMode.Acceleration"/> so they're independent of each prop's Mass —
	/// a light rubber duck and a heavy floating crate settle at the same depth, and tuning is intuitive
	/// (compare the buoyancy values directly against gravity's ~9.81).
	/// </summary>
	[RequireComponent(typeof(Collider))]
	public sealed class WaterVolume : MonoBehaviour
	{
		[Header("Surface")]
		[Tooltip("Optional. Transform whose Y is treated as the water surface (e.g. the water plane visual). " +
		         "If null, the top of this trigger collider's bounds is used.")]
		[SerializeField] private Transform _surfaceOverride;

		[Header("Buoyancy (upward acceleration at full submersion, vs gravity ~9.81)")]
		[Tooltip("Applied to bodies whose Floats == true. Must exceed gravity (~9.81) so they rise. " +
		         "They settle where buoyancy balances gravity, i.e. submerged fraction ≈ 9.81 / this.")]
		[SerializeField] private float _floatBuoyancy = 22f;
		[Tooltip("Applied to bodies whose Floats == false. Below gravity (~9.81) so they still sink, " +
		         "just slowed by the water. 0 = sinks at full weight.")]
		[SerializeField] private float _sinkBuoyancy = 4f;

		[Header("Water resistance (while submerged)")]
		[Tooltip("Linear drag acceleration per (m/s) of velocity. Damps the bob so floaters settle and " +
		         "sinkers drift down gently instead of free-falling.")]
		[SerializeField] private float _linearDrag = 3f;
		[Tooltip("Fraction of angular velocity bled off per second while submerged, so things stop spinning in water.")]
		[SerializeField] private float _angularDrag = 2f;

		[Header("Self-righting (floating bodies)")]
		[Tooltip("Angular acceleration that rolls a floating body's local up-axis back toward world up, so a " +
		         "floater (e.g. the rubber duck) settles upright at the surface instead of tumbling. " +
		         "Torque scales with how far it's tilted (zero when level) and how submerged it is; the " +
		         "Angular drag above damps the settle so it doesn't oscillate. 0 = no self-righting.")]
		[SerializeField] private float _uprightTorque = 8f;

		private Collider _trigger;

		// One entry per buoyant body currently overlapping the water, keyed by its Rigidbody.
		// We keep the specific child collider that triggered the overlap to estimate submersion depth.
		private readonly struct Tracked
		{
			public readonly IBuoyantBody Body;
			public readonly Collider Collider;
			public Tracked(IBuoyantBody body, Collider collider) { Body = body; Collider = collider; }
		}

		private readonly Dictionary<Rigidbody, Tracked> _bodies = new Dictionary<Rigidbody, Tracked>();
		private readonly List<Rigidbody> _stale = new List<Rigidbody>();

		private void Awake()
		{
			_trigger = GetComponent<Collider>();
			if (_trigger != null && _trigger.isTrigger == false)
				Debug.LogWarning($"{name}: WaterVolume collider should be a trigger so objects fall into it instead of bouncing off.", this);
		}

		private void OnTriggerEnter(Collider other)
		{
			var rb = other.attachedRigidbody;
			if (rb == null) return;

			// One GetComponent at entry, not every physics step. Non-buoyant rigidbodies (players, etc.)
			// just aren't tracked, so they're unaffected by the water.
			var body = rb.GetComponent<IBuoyantBody>();
			if (body == null) return;

			_bodies[rb] = new Tracked(body, other);
		}

		private void OnTriggerExit(Collider other)
		{
			var rb = other.attachedRigidbody;
			if (rb == null) return;

			// Only forget the body if the collider leaving is the one we were tracking it by — a body
			// with several colliders shouldn't drop out the moment any single one clears the surface.
			if (_bodies.TryGetValue(rb, out var tracked) && tracked.Collider == other)
				_bodies.Remove(rb);
		}

		private void FixedUpdate()
		{
			if (_bodies.Count == 0) return;

			float surfaceY = _surfaceOverride != null ? _surfaceOverride.position.y : _trigger.bounds.max.y;
			float dt = Time.fixedDeltaTime;

			foreach (var pair in _bodies)
			{
				var rb = pair.Key;
				var tracked = pair.Value;

				// Despawned/destroyed, or a proxy (kinematic): nothing for us to simulate. Collect for cleanup.
				if (rb == null || tracked.Collider == null || rb.isKinematic)
				{
					_stale.Add(rb);
					continue;
				}

				// Settled at the surface and asleep: leave it. Re-applying buoyancy would just wake it and
				// keep it (and its replicated transform) jittering forever. A collision or any force wakes it
				// again automatically, and we resume buoyancy on the next tick. Keep it tracked, just skip.
				if (rb.IsSleeping()) continue;

				var b = tracked.Collider.bounds;
				float objectBottom = b.min.y;
				float objectHeight = Mathf.Max(b.size.y, 0.01f);

				// 0 when the object is sitting entirely above the surface, 1 when fully under it.
				float submerged = Mathf.Clamp01((surfaceY - objectBottom) / objectHeight);
				if (submerged <= 0f) continue;

				float buoyancy = (tracked.Body.Floats ? _floatBuoyancy : _sinkBuoyancy) * submerged;
				rb.AddForce(Vector3.up * buoyancy, ForceMode.Acceleration);

				// Water resistance, scaled by how submerged the body is.
				rb.AddForce(-rb.linearVelocity * (_linearDrag * submerged), ForceMode.Acceleration);
				rb.angularVelocity *= Mathf.Clamp01(1f - _angularDrag * submerged * dt);

				// Self-righting: a gentle torque that rolls a floating body upright (local up → world up)
				// so the duck settles instead of spinning. Cross(up, worldUp) is the rotation axis and its
				// magnitude is sin(tilt) — strong when capsized, fading to zero as it levels off. The
				// angular drag above damps the swing so it eases in rather than oscillating.
				if (tracked.Body.Floats && _uprightTorque > 0f)
				{
					Vector3 righting = Vector3.Cross(rb.transform.up, Vector3.up);
					rb.AddTorque(righting * (_uprightTorque * submerged), ForceMode.Acceleration);
				}
			}

			if (_stale.Count > 0)
			{
				foreach (var rb in _stale) _bodies.Remove(rb);
				_stale.Clear();
			}
		}

		private void OnDrawGizmosSelected()
		{
			var col = GetComponent<Collider>();
			if (col == null) return;
			Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.25f);
			Gizmos.DrawCube(col.bounds.center, col.bounds.size);
			float surfaceY = _surfaceOverride != null ? _surfaceOverride.position.y : col.bounds.max.y;
			Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.9f);
			var c = col.bounds.center;
			Gizmos.DrawLine(new Vector3(col.bounds.min.x, surfaceY, c.z), new Vector3(col.bounds.max.x, surfaceY, c.z));
		}
	}
}
