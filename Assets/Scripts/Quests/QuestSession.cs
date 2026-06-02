using System;
using Starter.Common.Input;
using Starter.Common.Interactions;
using Starter.Common.Menu;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Local-only orchestrator for "having a quest giver open". Lives on the Player prefab next to
	/// <see cref="GameInputActions"/> + <see cref="InputContextController"/>. Mirrors
	/// <see cref="CraftingSession"/>: the open/close transition is purely local, the giver itself
	/// is multi-user and stores no per-opener state (only first-come completion).
	///
	/// Registers as an <see cref="IMenuScreen"/> while open so <see cref="MenuManager"/> routes Escape to it.
	/// </summary>
	[RequireComponent(typeof(GameInputActions))]
	[RequireComponent(typeof(InputContextController))]
	public sealed class QuestSession : MonoBehaviour, IInteractionGate, IMenuScreen
	{
		string IMenuScreen.MenuName => "Quest";
		bool IMenuScreen.DismissOnEscape => true;
		void IMenuScreen.CloseFromMenu() => RequestClose();

		[Tooltip("Auto-close margin: when the local player walks further than InteractRange * this multiplier from the open giver, the menu closes.")]
		public float AutoCloseRangeMultiplier = 1.5f;

		bool IInteractionGate.AllowInteractions => Current == null;

		public QuestGiver Current { get; private set; }

		/// <summary>True while ANY local QuestSession has an open giver. Polled by UI gates to suppress menu toggles during a turn-in.</summary>
		public static bool IsAnyOpen { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			IsAnyOpen = false;
		}

		/// <summary>Fires when the open giver changes. Argument is null when closed.</summary>
		public event Action<QuestGiver> OpenedChanged;

		private InputContextController _context;
		private bool _initialized;

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
				IsAnyOpen = false;
				MenuManager.Instance?.Close(this);
			}
			Current = null;
		}

		private void Update()
		{
			if (Current == null) return;

			// Auto-close if the player wanders away — the giver has no networked lock,
			// so each opener polices its own range.
			float allowed = Current.InteractRange * AutoCloseRangeMultiplier;
			if ((Current.transform.position - transform.position).sqrMagnitude > allowed * allowed)
			{
				RequestClose();
			}
		}

		/// <summary>Called by <see cref="QuestGiver"/> when the local scanner fires Interact near it.</summary>
		public void TryOpen(QuestGiver giver)
		{
			if (!_initialized || giver == null) return;
			if (Current != null) return;

			Current = giver;
			IsAnyOpen = true;
			if (_context != null) _context.EnterInventoryMode();
			MenuManager.Instance?.Open(this);
			// Tell the NPC (if it's an NpcAgent) to stop and face us during the turn-in.
			giver.GetComponent<NpcAgent>()?.LocalBeginAttending();
			OpenedChanged?.Invoke(Current);
			Debug.Log($"[QuestSession] Opened '{giver.DisplayName}'.");
		}

		public void RequestClose()
		{
			if (Current == null) return;

			var closed = Current;
			Current = null;
			IsAnyOpen = false;
			MenuManager.Instance?.Close(this);
			if (_context != null) _context.EnterPlayerMode();
			closed.GetComponent<NpcAgent>()?.LocalEndAttending();
			OpenedChanged?.Invoke(null);
			Debug.Log($"[QuestSession] Closed '{closed.DisplayName}'.");
		}

		/// <summary>Forward a turn-in request to the open giver's state authority. UI should pre-gate via inventory + claim state.</summary>
		public void RequestComplete(int offerIndex)
		{
			if (Current == null) return;
			Current.RPC_RequestComplete(offerIndex);
		}
	}
}
