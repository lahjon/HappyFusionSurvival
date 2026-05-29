using System;
using Starter.Common.Input;
using Starter.Common.Interactions;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only orchestrator for "lying in a bed". Lives on the Player prefab next to the
	/// other session components. Mirrors <see cref="ComputerSession"/>:
	///   - <see cref="IInteractionGate.AllowInteractions"/> gates the scanner off while in bed
	///   - lerps the local camera from its current pose to <see cref="Bed.SleepViewTransform"/>
	///     on enter, and back out on wake
	/// Drives its phase off the networked <see cref="Player.IsSleeping"/> flag so the host's
	/// "everyone asleep -> morning" auto-wake also unwinds the local camera lerp.
	/// </summary>
	[RequireComponent(typeof(GameInputActions))]
	[RequireComponent(typeof(InputContextController))]
	[RequireComponent(typeof(Player))]
	public sealed class SleepSession : MonoBehaviour, IInteractionGate
	{
		bool IInteractionGate.AllowInteractions => CurrentBed == null;

		public Bed CurrentBed { get; private set; }

		/// <summary>True while any local SleepSession is docked or animating. Read by <c>Player.LateUpdate</c> to skip its camera write so this component owns the camera.</summary>
		public static bool IsAnySleeping { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			IsAnySleeping = false;
		}

		/// <summary>Fires when the bed we're sleeping in changes. Argument is null when waking.</summary>
		public event Action<Bed> OpenedChanged;

		private enum CamPhase { Idle, ZoomingIn, Docked, ZoomingOut }

		private GameInputActions _actions;
		private Player _player;
		private bool _initialized;
		private bool _wasSleeping;

		private CamPhase _phase = CamPhase.Idle;
		private float _phaseElapsed;
		private Vector3 _startPosition;
		private Quaternion _startRotation;

		public void Initialize()
		{
			if (_initialized) return;

			_actions = GetComponent<GameInputActions>();
			_player = GetComponent<Player>();

			_initialized = true;
		}

		private void OnDestroy()
		{
			if (CurrentBed != null) IsAnySleeping = false;
			CurrentBed = null;
		}

		private void Update()
		{
			if (!_initialized) return;

			// React to the networked sleep flag flipping off — covers both the local "press E to wake"
			// path (we sent the RPC, the bit clears, we zoom out) and the host's "morning" auto-wake
			// (we never sent anything, but our SleepSession still needs to release the camera).
			bool sleepingNow = _player != null && _player.IsSleeping;
			if (_wasSleeping && sleepingNow == false && CurrentBed != null && _phase != CamPhase.Idle && _phase != CamPhase.ZoomingOut)
			{
				BeginClose();
			}
			_wasSleeping = sleepingNow;

			// Press Interact while fully docked to wake. Polling (not subscribing) avoids racing with the
			// InteractionScanner's own Interact subscriber on the same frame the player presses E to enter.
			if (_phase == CamPhase.Docked && CurrentBed != null
				&& _actions != null && _actions.IsInitialized && _actions.Interact.WasPressedThisFrame())
			{
				RequestWake();
			}
		}

		private void LateUpdate()
		{
			if (_phase == CamPhase.Idle || CurrentBed == null) return;

			var cam = Camera.main;
			if (cam == null) return;

			Transform view = CurrentBed.SleepViewTransform;
			if (view == null)
			{
				// Authoring error — bail to a clean state rather than freezing the camera.
				BeginClose();
				return;
			}

			switch (_phase)
			{
				case CamPhase.ZoomingIn:
				{
					_phaseElapsed += Time.deltaTime;
					float duration = Mathf.Max(0.0001f, CurrentBed.ZoomDuration);
					float u = Mathf.Clamp01(_phaseElapsed / duration);
					float k = CurrentBed.ZoomEase != null ? CurrentBed.ZoomEase.Evaluate(u) : u;

					cam.transform.SetPositionAndRotation(
						Vector3.Lerp(_startPosition, view.position, k),
						Quaternion.Slerp(_startRotation, view.rotation, k));

					if (u >= 1f) _phase = CamPhase.Docked;
					break;
				}
				case CamPhase.Docked:
				{
					cam.transform.SetPositionAndRotation(view.position, view.rotation);
					break;
				}
				case CamPhase.ZoomingOut:
				{
					_phaseElapsed += Time.deltaTime;
					float duration = Mathf.Max(0.0001f, CurrentBed.ZoomDuration);
					float u = Mathf.Clamp01(_phaseElapsed / duration);
					float k = CurrentBed.ZoomEase != null ? CurrentBed.ZoomEase.Evaluate(u) : u;

					cam.transform.SetPositionAndRotation(
						Vector3.Lerp(view.position, _startPosition, k),
						Quaternion.Slerp(view.rotation, _startRotation, k));

					if (u >= 1f) FinishClose();
					break;
				}
			}
		}

		/// <summary>Called by <see cref="Bed.OnInteract"/> when the local scanner fires Interact on it.</summary>
		public void TryOpen(Bed bed)
		{
			if (!_initialized || bed == null) return;
			if (CurrentBed != null) return;
			if (bed.SleepViewTransform == null) return;

			// Local pre-checks; the host re-validates inside RPC_RequestSleep.
			var time = TimeManager.Instance;
			if (time != null && time.IsNight == false) return;
			if (bed.HasOccupant) return;

			CurrentBed = bed;
			IsAnySleeping = true;

			var cam = Camera.main;
			if (cam != null)
			{
				_startPosition = cam.transform.position;
				_startRotation = cam.transform.rotation;
			}

			_phase = CamPhase.ZoomingIn;
			_phaseElapsed = 0f;

			bed.LocalRequestSleep();

			OpenedChanged?.Invoke(CurrentBed);
			Debug.Log($"[SleepSession] Sleeping in '{bed.DisplayName}'.");
		}

		public void RequestWake()
		{
			if (CurrentBed == null) return;
			if (_phase == CamPhase.ZoomingOut) return;

			// Fire-and-forget RPC. Actual zoom-out begins when the networked IsSleeping flips back to
			// false (handled in Update), so player-initiated and host-initiated wakes share one path.
			CurrentBed.LocalRequestWake();
		}

		private void BeginClose()
		{
			_phase = CamPhase.ZoomingOut;
			_phaseElapsed = 0f;
		}

		private void FinishClose()
		{
			var closed = CurrentBed;
			CurrentBed = null;
			IsAnySleeping = false;
			_phase = CamPhase.Idle;

			OpenedChanged?.Invoke(null);
			Debug.Log($"[SleepSession] Woke up from '{(closed != null ? closed.DisplayName : "<null>")}'.");
		}
	}
}
