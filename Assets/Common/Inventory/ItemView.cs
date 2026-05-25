using UnityEngine;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// Local-only visual for an item. Used both as a child of a world PickupableItem
	/// (with optional bob/spin flair) and as the hand-held model parented to the
	/// player's HandAnchor. Not networked — driven entirely by local state.
	/// </summary>
	public sealed class ItemView : MonoBehaviour
	{
		[Header("World flair (ignored on hand-held)")]
		public bool Bob;
		public float BobAmplitude = 0.1f;
		public float BobFrequency = 1f;
		public bool Rotate;
		public float RotateSpeed = 60f;

		private Vector3 _basePos;

		private void Awake()
		{
			_basePos = transform.localPosition;
		}

		private void Update()
		{
			if (Bob)
			{
				float y = Mathf.Sin(Time.time * BobFrequency * Mathf.PI * 2f) * BobAmplitude;
				transform.localPosition = _basePos + new Vector3(0f, y, 0f);
			}

			if (Rotate)
			{
				transform.Rotate(0f, RotateSpeed * Time.deltaTime, 0f, Space.Self);
			}
		}
	}
}
