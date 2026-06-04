using System;
using Starter.Common.Input;
using Starter.Common.Interactions;
using Starter.Common.Menu;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Shared base for local-only "open a station's menu" sessions (crafting bench, shop, quest giver).
	/// Lives on the Player prefab next to <see cref="GameInputActions"/> + <see cref="InputContextController"/>.
	/// The open/close transition is purely local; the station itself is multi-user and stores no per-opener
	/// state (concurrent users just race on the state authority).
	///
	/// Captures the boilerplate that <c>CraftingSession</c>/<c>ShopSession</c>/<c>QuestSession</c> used to
	/// duplicate verbatim: <see cref="Initialize"/>/<see cref="OnDestroy"/>/<see cref="Update"/> auto-close,
	/// the <see cref="InputContextController"/> toggle, <see cref="MenuManager"/> Escape registration, the
	/// <see cref="Current"/> property and <see cref="OpenedChanged"/> event. Subclasses supply only the
	/// station-specific bits through the small hook surface below.
	///
	/// <para><b>Not</b> the base for <c>SleepSession</c>/<c>ComputerSession</c> — those own the local camera
	/// and have modal / animated lifecycles that don't fit the plain "just open a UI" shape.</para>
	///
	/// Registers as an <see cref="IMenuScreen"/> while open so <see cref="MenuManager"/> routes Escape to it.
	/// </summary>
	[RequireComponent(typeof(GameInputActions))]
	[RequireComponent(typeof(InputContextController))]
	public abstract class InteractableSession<TStation> : MonoBehaviour, IInteractionGate, IMenuScreen
		where TStation : InteractableStation
	{
		[Tooltip("Auto-close margin: when the local player walks further than InteractRange * this multiplier from the open station, the menu closes.")]
		public float AutoCloseRangeMultiplier = 1.5f;

		/// <summary>The station this session currently has open, or null.</summary>
		public TStation Current { get; private set; }

		/// <summary>Fires when the open station changes. Argument is null when closed.</summary>
		public event Action<TStation> OpenedChanged;

		// ─── Subclass hooks ──────────────────────────────────────────────────

		/// <summary>Human-readable label used as the <see cref="IMenuScreen.MenuName"/> (e.g. "Shop").</summary>
		protected abstract string Label { get; }

		/// <summary>Write the subclass's "is any open" static flag. Statics can't be virtual, so each subclass
		/// routes its own flag through here.</summary>
		protected abstract void SetAnyOpen(bool value);

		/// <summary>Called after a station opens (e.g. tell an <c>NpcAgent</c> to attend). Default: no-op.</summary>
		protected virtual void OnOpened(TStation station) { }

		/// <summary>Called after a station closes. Default: no-op.</summary>
		protected virtual void OnClosed(TStation station) { }

		// ─── IMenuScreen ─────────────────────────────────────────────────────

		string IMenuScreen.MenuName => Label;
		bool IMenuScreen.DismissOnEscape => true;
		void IMenuScreen.CloseFromMenu() => RequestClose();

		// ─── IInteractionGate ────────────────────────────────────────────────

		bool IInteractionGate.AllowInteractions => Current == null;

		private InputContextController _context;
		private bool _initialized;

		/// <summary>Wires the input-context controller. Called by <c>PlayerInput</c> on spawn. Idempotent.</summary>
		public void Initialize()
		{
			if (_initialized) return;

			_context = GetComponent<InputContextController>();

			_initialized = true;
		}

		protected virtual void OnDestroy()
		{
			if (!_initialized) return;

			if (Current != null)
			{
				SetAnyOpen(false);
				MenuManager.Instance?.Close(this);
			}
			Current = null;
		}

		protected virtual void Update()
		{
			if (Current == null) return;

			// Auto-close if the player wanders away — the station has no networked lock,
			// so each opener polices its own range.
			float allowed = Current.InteractRange * AutoCloseRangeMultiplier;
			if ((Current.transform.position - transform.position).sqrMagnitude > allowed * allowed)
				RequestClose();
		}

		/// <summary>Called by the station when the local scanner fires Interact near it.</summary>
		public void TryOpen(TStation station)
		{
			if (!_initialized || station == null) return;
			if (Current != null) return;

			Current = station;
			SetAnyOpen(true);
			_context?.EnterInventoryMode();
			MenuManager.Instance?.Open(this);
			OnOpened(station);
			OpenedChanged?.Invoke(Current);
			Debug.Log($"[{GetType().Name}] Opened '{station.DisplayName}'.");
		}

		public void RequestClose()
		{
			if (Current == null) return;

			var closed = Current;
			Current = null;
			SetAnyOpen(false);
			MenuManager.Instance?.Close(this);
			_context?.EnterPlayerMode();
			OnClosed(closed);
			OpenedChanged?.Invoke(null);
			Debug.Log($"[{GetType().Name}] Closed '{closed.DisplayName}'.");
		}
	}
}
