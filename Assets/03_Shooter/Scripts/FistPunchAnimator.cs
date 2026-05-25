using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Authoring component on the Fist hand prefab. Marks the held instance as a punch
	/// (no projectile, short range) and animates the fist forward-back when Punch() is called.
	/// </summary>
	public sealed class FistPunchAnimator : MonoBehaviour
	{
		[Tooltip("Local-space offset the fist travels at the peak of the punch.")]
		public Vector3 PunchOffset = new Vector3(0f, 0f, 0.3f);

		[Tooltip("Total duration of the forward-and-back punch animation in seconds.")]
		public float PunchDuration = 0.18f;

		private float _t = -1f;
		private Vector3 _restPosition;

		public void Punch()
		{
			_t = 0f;
		}

		private void Start()
		{
			_restPosition = transform.localPosition;
		}

		private void Update()
		{
			if (_t < 0f) return;

			_t += Time.deltaTime;
			float u = Mathf.Clamp01(_t / PunchDuration);
			float k = u < 0.5f ? u * 2f : (1f - u) * 2f;
			transform.localPosition = _restPosition + PunchOffset * k;

			if (u >= 1f)
			{
				_t = -1f;
				transform.localPosition = _restPosition;
			}
		}
	}
}
