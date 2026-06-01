using Starter.Common.Interactions;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Locks an interactable — and optionally slams an <see cref="AnimatedInteractable"/> shut —
	/// for the night half of the round. Matches the design beat in CLAUDE.md: when the Purge begins,
	/// "vendors gone, doors shut". Drop this next to a <see cref="BaseInteractable"/> (a door, a shop,
	/// a quest giver) and it goes uninteractable for the chosen phases.
	///
	/// Phase-aware per the project rule: reads <see cref="MatchManager.PhaseChanged"/> — the single
	/// networked source of truth — never local <c>Time.time</c>. The lock itself is purely local
	/// (it flips <see cref="BaseInteractable.Interactable"/>, which every peer derives identically from
	/// the replicated phase). The force-close routes through <see cref="AnimatedInteractable.ForceSetState"/>,
	/// which no-ops on non-authority peers, so only the host writes the networked door state and it
	/// replicates from there. Cosmetic/derived, so a plain <see cref="MonoBehaviour"/>.
	/// </summary>
	public sealed class PhaseLockedInteractable : MonoBehaviour
	{
		[Header("References (auto-found if null)")]
		[Tooltip("The interactable to lock. Its Interactable flag is toggled off during locked phases.")]
		[SerializeField] private BaseInteractable _interactable;

		[Tooltip("Optional animated door/lid to force shut when the lock engages.")]
		[SerializeField] private AnimatedInteractable _animated;

		[Header("Lock window")]
		[Tooltip("Lock + close once the Purge proper begins.")]
		[SerializeField] private bool _lockAtNight = true;

		[Tooltip("Lock + close already during the DuskWarning transition (recommended — doors shut as night falls).")]
		[SerializeField] private bool _lockAtDusk = true;

		[Header("Effects while locked")]
		[Tooltip("Disable interaction (CanInteract) during the locked phases.")]
		[SerializeField] private bool _disableInteraction = true;

		[Tooltip("Force the animated door shut when the lock engages (authority drives the networked state).")]
		[SerializeField] private bool _closeOnLock = true;

		[Tooltip("Toast shown when a player tries to interact while locked. Leave empty for silent.")]
		[SerializeField] private string _lockedReason = "Sealed for the night";

		private string _unlockedReason;

		private void Reset()
		{
			_interactable = GetComponent<BaseInteractable>();
			_animated     = GetComponent<AnimatedInteractable>();
		}

		private void OnEnable()
		{
			if (_interactable == null) _interactable = GetComponent<BaseInteractable>();
			if (_animated == null)     _animated     = GetComponent<AnimatedInteractable>();

			if (_interactable != null)
				_unlockedReason = _interactable.LockedReasonText;

			MatchManager.PhaseChanged += Apply;

			// Apply the current phase immediately — covers being enabled after MatchManager spawned, and late joiners.
			Apply(MatchManager.Instance != null ? MatchManager.Instance.Phase : MatchPhase.Lobby);
		}

		private void OnDisable()
		{
			MatchManager.PhaseChanged -= Apply;
		}

		private void Apply(MatchPhase phase)
		{
			bool locked = (phase == MatchPhase.Night       && _lockAtNight)
			           || (phase == MatchPhase.DuskWarning  && _lockAtDusk);

			if (_interactable != null && _disableInteraction)
			{
				_interactable.Interactable    = !locked;
				_interactable.LockedReasonText = locked ? _lockedReason : _unlockedReason;
			}

			// Only slam shut on the rising edge into a locked phase; ForceSetState no-ops off-authority.
			if (locked && _closeOnLock && _animated != null)
				_animated.ForceSetState(false);
		}
	}
}
