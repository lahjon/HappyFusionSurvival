using System;
using Starter.Common.Input;
using Starter.Common.Interactions;
using Starter.Common.Menu;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only orchestrator for "sitting at a computer". Lives on the Player prefab
	/// next to <see cref="GameInputActions"/> + <see cref="InputContextController"/>.
	/// Mirrors <see cref="CraftingSession"/>, plus owns the camera while docked: lerps
	/// from the player's current view to the computer's <c>ScreenViewTransform</c>,
	/// then back out on close. While docked, <c>Player.LateUpdate</c> skips its own
	/// camera write (gated by <see cref="IsAnyAtComputer"/>).
	/// </summary>
	[RequireComponent(typeof(GameInputActions))]
	[RequireComponent(typeof(InputContextController))]
	public sealed class ComputerSession : MonoBehaviour, IInteractionGate, IMenuScreen
	{
		string IMenuScreen.MenuName => "Computer";
		bool IMenuScreen.DismissOnEscape => true;
		void IMenuScreen.CloseFromMenu() => RequestClose();

		[Tooltip("Auto-close margin: when the local player walks further than the computer's InteractRange * this multiplier, the session closes.")]
		public float AutoCloseRangeMultiplier = 1.5f;

		bool IInteractionGate.AllowInteractions => Current == null;

		public Computer Current { get; private set; }

		/// <summary>True while any local ComputerSession is docked or animating. Read by <c>Player.LateUpdate</c> to skip its camera write so this component owns the camera.</summary>
		public static bool IsAnyAtComputer { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			IsAnyAtComputer = false;
		}

		/// <summary>Fires when the open computer changes. Argument is null when closed.</summary>
		public event Action<Computer> OpenedChanged;

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
				IsAnyAtComputer = false;
				MenuManager.Instance?.Close(this);
			}
			Current = null;
		}

		private void Update()
		{
			if (Current == null) return;

			// Auto-close if the player drifts away. Only meaningful while fully docked —
			// during the zoom transitions the player is locked in place.
			if (_phase != CamPhase.Docked) return;

			float allowed = Current.InteractRange * AutoCloseRangeMultiplier;
			if ((Current.transform.position - transform.position).sqrMagnitude > allowed * allowed)
			{
				RequestClose();
			}
		}

		private void LateUpdate()
		{
			// Drive the camera while we own it. Runs every frame regardless of script order
			// because Player.LateUpdate skips its own camera write when IsAnyAtComputer is true.
			if (_phase == CamPhase.Idle || Current == null) return;

			var cam = Camera.main;
			if (cam == null) return;

			Transform screen = Current.ScreenViewTransform;
			if (screen == null)
			{
				// Authoring error — bail to a clean state rather than freezing the camera.
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
						Vector3.Lerp(_startPosition, screen.position, k),
						Quaternion.Slerp(_startRotation, screen.rotation, k));

					if (u >= 1f) _phase = CamPhase.Docked;
					break;
				}
				case CamPhase.Docked:
				{
					cam.transform.SetPositionAndRotation(screen.position, screen.rotation);
					break;
				}
				case CamPhase.ZoomingOut:
				{
					_phaseElapsed += Time.deltaTime;
					float duration = Mathf.Max(0.0001f, Current.ZoomDuration);
					float u = Mathf.Clamp01(_phaseElapsed / duration);
					float k = Current.ZoomEase != null ? Current.ZoomEase.Evaluate(u) : u;

					cam.transform.SetPositionAndRotation(
						Vector3.Lerp(screen.position, _startPosition, k),
						Quaternion.Slerp(screen.rotation, _startRotation, k));

					if (u >= 1f)
					{
						FinishClose();
					}
					break;
				}
			}
		}

		/// <summary>Called by <see cref="Computer.OnInteract"/> when the local scanner fires Interact near it.</summary>
		public void TryOpen(Computer computer)
		{
			if (!_initialized || computer == null) return;
			if (Current != null) return;
			if (computer.ScreenViewTransform == null) return;

			Current = computer;
			IsAnyAtComputer = true;

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
			Debug.Log($"[ComputerSession] Opened '{computer.DisplayName}'.");
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
			IsAnyAtComputer = false;
			_phase = CamPhase.Idle;

			MenuManager.Instance?.Close(this);
			if (_context != null) _context.EnterPlayerMode();

			OpenedChanged?.Invoke(null);
			Debug.Log($"[ComputerSession] Closed '{(closed != null ? closed.DisplayName : "<null>")}'.");
		}
	}
}
