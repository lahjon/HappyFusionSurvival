using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Authoring component on a weapon's HandPrefab. Marks the prefab as a usable weapon
	/// and carries tuning that Player.Fire reads — range, damage, and (for melee) a
	/// procedural swing animation. Inventory moves the whole instance to the
	/// FirstPersonOverlay layer when held by the local player.
	/// </summary>
	public sealed class HeldWeapon : MonoBehaviour
	{
		[Header("Combat")]
		[Tooltip("Hitscan range in meters. Ranged weapons (pistol) use ~200; melee weapons (bat) use ~2.")]
		public float Range = 200f;
		[Tooltip("Damage dealt per hit.")]
		public int Damage = 1;
		[Tooltip("If true, Fire triggers Swing() instead of muzzle FX; no fire sound is played.")]
		public bool IsMelee = false;

		[Header("Ranged FX")]
		[Tooltip("Plays when the weapon fires. Ranged only.")]
		public ParticleSystem MuzzleParticle;

		[Header("Melee Swing")]
		[Tooltip("Local-space rotation arc at the peak of the swing (in euler degrees).")]
		public Vector3 SwingArcEuler = new Vector3(-80f, 0f, 0f);
		[Tooltip("Total duration of the forward-and-back swing in seconds.")]
		public float SwingDuration = 0.28f;

		private float _t = -1f;
		private Quaternion _restRotation;
		private bool _restCaptured;

		private void OnEnable()
		{
			// HandPrefabs are Instantiated then parented to HandAnchor with localRotation reset,
			// so capture the rest pose once we're parented + active.
			_restRotation = transform.localRotation;
			_restCaptured = true;
		}

		public void Swing()
		{
			if (!IsMelee) return;
			if (!_restCaptured)
			{
				_restRotation = transform.localRotation;
				_restCaptured = true;
			}
			_t = 0f;
		}

		private void Update()
		{
			if (_t < 0f) return;

			_t += Time.deltaTime;
			float u = Mathf.Clamp01(_t / Mathf.Max(0.01f, SwingDuration));
			float k = u < 0.5f ? u * 2f : (1f - u) * 2f;
			transform.localRotation = _restRotation * Quaternion.Euler(SwingArcEuler * k);

			if (u >= 1f)
			{
				_t = -1f;
				transform.localRotation = _restRotation;
			}
		}
	}
}
