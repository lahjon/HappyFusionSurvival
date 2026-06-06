using System;
using Starter.Common.Input;
using Starter.Common.Interactions;
using Starter.Common.Menu;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only orchestrator for "operating a padlock". Lives on the Player prefab next to
	/// <see cref="GameInputActions"/> + <see cref="InputContextController"/>. Mirrors
	/// <see cref="ComputerSession"/>: docks the local camera at the padlock's
	/// <c>KeypadViewTransform</c>, switches to the inventory input context (frees the cursor for
	/// keypad clicks), and registers with <see cref="MenuManager"/> so Escape closes it.
	///
	/// Typing the keypad and the green/red flash are local; only the authoritative code check and
	/// the resulting unlock are networked (see <see cref="Padlock"/>).
	/// </summary>
	[RequireComponent(typeof(GameInputActions))]
	[RequireComponent(typeof(InputContextController))]
	public sealed class PadlockSession : MonoBehaviour, IInteractionGate, IMenuScreen
	{
		string IMenuScreen.MenuName => "Padlock";
		bool IMenuScreen.DismissOnEscape => true;
		void IMenuScreen.CloseFromMenu() => RequestClose();

		[Tooltip("Auto-close margin: when the local player walks further than the padlock's InteractRange * this multiplier, the session closes.")]
		public float AutoCloseRangeMultiplier = 1.5f;

		bool IInteractionGate.AllowInteractions => Current == null;

		public Padlock Current { get; private set; }

		/// <summary>True while any local PadlockSession is docked or animating. Read by <c>Player.LateUpdate</c> to skip its camera write so this component owns the camera.</summary>
		public static bool IsAnyAtPadlock { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			IsAnyAtPadlock = false;
		}

		/// <summary>Fires when the open padlock changes. Argument is null when closed.</summary>
		public event Action<Padlock> OpenedChanged;

		private enum CamPhase { Idle, ZoomingIn, Docked, ZoomingOut }

		private InputContextController _context;
		private bool _initialized;

		private CamPhase _phase = CamPhase.Idle;
		private float _phaseElapsed;
		private Vector3 _startPosition;
		private Quaternion _startRotation;

		public void Initialize()
		{
			if (_initialized) return;

			_context = GetComponent<InputContextController>();

			_initialized = true;
		}

		private void OnDestroy()
		{
			if (!_initialized) return;

			if (Current != null)
			{
				IsAnyAtPadlock = false;
				MenuManager.Instance?.Close(this);
			}
			Current = null;
		}

		private void Update()
		{
			if (Current == null) return;

			// Auto-close only while fully docked — during zoom the player is locked in place.
			if (_phase != CamPhase.Docked) return;

			float allowed = Current.InteractRange * AutoCloseRangeMultiplier;
			if ((Current.transform.position - transform.position).sqrMagnitude > allowed * allowed)
			{
				RequestClose();
			}
		}

		private void LateUpdate()
		{
			// Drive the camera while we own it (Player.LateUpdate skips its write when IsAnyAtPadlock).
			if (_phase == CamPhase.Idle || Current == null) return;

			var cam = Camera.main;
			if (cam == null) return;

			Transform view = Current.KeypadViewTransform;
			if (view == null)
			{
				RequestClose();
				return;
			}

			switch (_phase)
			{
				case CamPhase.ZoomingIn:
				{
					_phaseElapsed += Time.deltaTime;
					float duration = Mathf.Max(0.0001f, Current.ZoomDuration);
					float u = Mathf.Clamp01(_phaseElapsed / duration);
					float k = Current.ZoomEase != null ? Current.ZoomEase.Evaluate(u) : u;

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
					float duration = Mathf.Max(0.0001f, Current.ZoomDuration);
					float u = Mathf.Clamp01(_phaseElapsed / duration);
					float k = Current.ZoomEase != null ? Current.ZoomEase.Evaluate(u) : u;

					cam.transform.SetPositionAndRotation(
						Vector3.Lerp(view.position, _startPosition, k),
						Quaternion.Slerp(view.rotation, _startRotation, k));

					if (u >= 1f)
					{
						FinishClose();
					}
					break;
				}
			}
		}

		/// <summary>Called by <see cref="Padlock.OnInteract"/> when the local scanner fires Interact near it.</summary>
		public void TryOpen(Padlock padlock)
		{
			if (!_initialized || padlock == null) return;
			if (Current != null) return;
			if (padlock.KeypadViewTransform == null) return;

			Current = padlock;
			IsAnyAtPadlock = true;

			var cam = Camera.main;
			if (cam != null)
			{
				_startPosition = cam.transform.position;
				_startRotation = cam.transform.rotation;
			}

			_phase = CamPhase.ZoomingIn;
			_phaseElapsed = 0f;

			if (_context != null) _context.EnterInventoryMode();
			MenuManager.Instance?.Open(this);

			OpenedChanged?.Invoke(Current);
			Debug.Log($"[PadlockSession] Opened '{padlock.DisplayName}'.");
		}

		/// <summary>Forward a typed keypad entry to the padlock's state authority for validation.</summary>
		public void Submit(string entry)
		{
			if (Current == null) return;
			Current.RPC_SubmitCode(entry, transform.position);
		}

		public void RequestClose()
		{
			if (Current == null) return;
			if (_phase == CamPhase.ZoomingOut) return;

			_phase = CamPhase.ZoomingOut;
			_phaseElapsed = 0f;
		}

		private void FinishClose()
		{
			var closed = Current;
			Current = null;
			IsAnyAtPadlock = false;
			_phase = CamPhase.Idle;

			MenuManager.Instance?.Close(this);
			if (_context != null) _context.EnterPlayerMode();

			OpenedChanged?.Invoke(null);
			Debug.Log($"[PadlockSession] Closed '{(closed != null ? closed.DisplayName : "<null>")}'.");
		}
	}
}
