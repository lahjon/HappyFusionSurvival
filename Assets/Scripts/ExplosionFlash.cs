using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only placeholder blast flash spawned by <see cref="GrenadeProjectile"/> at the detonation
	/// point. Eases its own scale toward zero and destroys itself once it's small enough. Purely
	/// cosmetic — a plain MonoBehaviour on Time.deltaTime, no networked state. Swap the prefab for a
	/// real VFX later; the grenade just Instantiates whatever is wired into its _explosionPrefab field.
	/// </summary>
	public sealed class ExplosionFlash : MonoBehaviour
	{
		[Tooltip("How fast the sphere collapses. Scale eases toward zero at this rate per second (higher = snappier).")]
		[SerializeField] private float _shrinkSpeed = 8f;

		[Tooltip("Uniform scale below which the flash is considered finished and the object destroys itself.")]
		[SerializeField] private float _destroyScale = 0.05f;

		private void Update()
		{
			Vector3 scale = Vector3.Lerp(transform.localScale, Vector3.zero, _shrinkSpeed * Time.deltaTime);
			transform.localScale = scale;

			if (scale.x <= _destroyScale)
				Destroy(gameObject);
		}
	}
}
