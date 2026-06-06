using Fusion;
using Starter.Common.Interactions;
using UnityEngine;
using UnityEngine.Events;

namespace Starter.Shooter
{
	/// <summary>
	/// Networked combination padlock station. On interact, hands off to the local
	/// <see cref="PadlockSession"/> which docks the camera at <see cref="KeypadViewTransform"/>
	/// and shows a keypad (digits 1-9 + Confirm). Typing the keypad is purely local UX; the
	/// authoritative check happens here on the state authority via <see cref="RPC_SubmitCode"/>.
	///
	/// One-shot, networked unlock: the first peer to enter the correct <see cref="Code"/> flips
	/// <see cref="IsUnlocked"/> for everyone and fires <see cref="OnUnlocked"/> once on the
	/// authority. Wrong attempts change no state; the submitter just gets a local red flash.
	/// </summary>
	public sealed class Padlock : InteractableStation
	{
		[Header("Combination")]
		[Tooltip("Expected combination, built from the 1-9 keypad. Digits only.")]
		public string Code = "1234";

		[Tooltip("Maximum number of digits the keypad will accept before Confirm.")]
		[Min(1)] public int MaxLength = 4;

		[Header("Camera")]
		[Tooltip("Pose the local camera lerps to when the player interacts. Author this transform on the prefab so it faces the keypad.")]
		public Transform KeypadViewTransform;

		[Tooltip("Seconds to ease from the player's normal camera pose into the keypad view (and back out on close).")]
		[Min(0f)] public float ZoomDuration = 0.45f;

		[Tooltip("Easing applied to the 0..1 lerp parameter during zoom in/out.")]
		public AnimationCurve ZoomEase = new AnimationCurve(
			new Keyframe(0f, 0f, 0f, 0f),
			new Keyframe(1f, 1f, 0f, 0f));

		[Header("Unlock")]
		[Tooltip("Raised once on the state authority when the correct code is accepted. Wire the door / lights / etc. here.")]
		public UnityEvent OnUnlocked;

		/// <summary>Replicated unlock result. State authority writes only. One-shot.</summary>
		[Networked, OnChangedRender(nameof(OnUnlockedRender))]
		public NetworkBool IsUnlocked { get; private set; }

		/// <summary>Local cue raised on every peer when <see cref="IsUnlocked"/> flips true.</summary>
		public event System.Action UnlockedChanged;

		// Still interactable once unlocked so the keypad can show the "open" (green) state.
		public override bool CanInteract => true;
		public override string LockedReason => string.Empty;

		protected override void OnInteract(InteractionScanner scanner)
		{
			if (KeypadViewTransform == null)
			{
				Debug.LogWarning($"[Padlock:{name}] KeypadViewTransform not assigned — cannot open.", this);
				return;
			}

			var session = scanner.GetComponent<PadlockSession>();
			if (session != null) session.TryOpen(this);
		}

		/// <summary>Standard zero-arg menu opener (menu-systems convention).</summary>
		public void OpenMenu()
		{
			var playerObj = Runner != null ? Runner.GetPlayerObject(Runner.LocalPlayer) : null;
			var session = playerObj != null
				? playerObj.GetComponent<PadlockSession>()
				: FindAnyObjectByType<PadlockSession>();
			session?.TryOpen(this);
		}

		/// <summary>
		/// Submit a correct entry for the authoritative networked unlock. The local green/red flash
		/// is handled client-side (the <see cref="Code"/> is serialized on every peer); this RPC is
		/// the source of truth that flips <see cref="IsUnlocked"/> for everyone and fires
		/// <see cref="OnUnlocked"/> once. The host re-validates both range and code — never trust the
		/// client's claim.
		/// </summary>
		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_SubmitCode(string entry, Vector3 playerPosition, RpcInfo info = default)
		{
			if (IsUnlocked)
				return;

			// Re-validate range and code host-side — never trust the client scan alone.
			if (!IsWithinHostRange(playerPosition))
				return;

			if (string.IsNullOrEmpty(entry) || entry != Code)
				return;

			IsUnlocked = true;
			OnUnlocked?.Invoke();
		}

		private void OnUnlockedRender()
		{
			if (IsUnlocked) UnlockedChanged?.Invoke();
		}
	}
}
