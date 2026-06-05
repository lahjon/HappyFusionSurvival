using TMPro;
using UnityEngine;

namespace Starter
{
	/// <summary>
	/// Component that handle showing nicknames above player
	/// </summary>
	public class UINameplate : MonoBehaviour
	{
		public TextMeshProUGUI NicknameText;

		private Transform _cameraTransform;

		public void SetNickname(string nickname)
		{
			if (NicknameText != null)
				NicknameText.text = nickname;
		}

		private void Awake()
		{
			// NOTE: do NOT cache Camera.main here. The nameplate GameObject is activated at runtime (the moment a
			// remote Player learns its nickname), and Camera.main can momentarily be null at that point — dereferencing
			// it would throw straight out of Player.Spawned. Resolve the camera lazily in LateUpdate instead.
			if (NicknameText != null)
				NicknameText.text = string.Empty;
		}

		private void LateUpdate()
		{
			// Rotate nameplate toward camera. Resolve the camera lazily and tolerate it being absent for a frame.
			if (_cameraTransform == null)
			{
				var cam = Camera.main;
				if (cam == null)
					return;
				_cameraTransform = cam.transform;
			}

			transform.rotation = _cameraTransform.rotation;
		}
	}
}
