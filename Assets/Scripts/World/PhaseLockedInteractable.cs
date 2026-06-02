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

		[Header("Electricity")]
		[Tooltip("Also lock + close whenever this object's LightGrid zone loses power, independent of the phase. " +
			"Lets devices die when the Purge blackout reaches them, not just at a fixed phase.")]
		[SerializeField] private bool _lockWhenZoneDark = true;

		[Tooltip("Toast shown when locked because the zone lost power.")]
		[SerializeField] private string _unpoweredReason = "No power";

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

			MatchManager.PhaseChanged += OnPhaseChanged;
			LightGrid.PowerChanged    += Reevaluate;

			// Apply current state immediately — covers being enabled after the managers spawned, and late joiners.
			Reevaluate();
		}

		private void OnDisable()
		{
			MatchManager.PhaseChanged -= OnPhaseChanged;
			LightGrid.PowerChanged    -= Reevaluate;
		}

		private void OnPhaseChanged(MatchPhase _) => Reevaluate();

		/// <summary>Recompute the lock from the two networked sources of truth — the match phase and this object's
		/// grid zone power. Both are derived identically on every peer, so the lock stays consistent without any
		/// extra networking; only the authority writes the forced-close door state (ForceSetState no-ops elsewhere).</summary>
		private void Reevaluate()
		{
			MatchPhase phase = MatchManager.Instance != null ? MatchManager.Instance.Phase : MatchPhase.Lobby;

			bool phaseLocked = (phase == MatchPhase.Night       && _lockAtNight)
			                || (phase == MatchPhase.DuskWarning  && _lockAtDusk);

			bool zoneDark = _lockWhenZoneDark
				&& LightGrid.Instance != null
				&& LightGrid.Instance.GetZonePowerAt(transform.position) <= 0f;

			bool locked = phaseLocked || zoneDark;

			if (_interactable != null && _disableInteraction)
			{
				_interactable.Interactable = !locked;
				if (locked)
					_interactable.LockedReasonText = zoneDark && !phaseLocked ? _unpoweredReason : _lockedReason;
				else
					_interactable.LockedReasonText = _unlockedReason;
			}

			// Slam shut while locked; ForceSetState no-ops off-authority so only the host writes the networked state.
			if (locked && _closeOnLock && _animated != null)
				_animated.ForceSetState(false);
		}
	}
}
