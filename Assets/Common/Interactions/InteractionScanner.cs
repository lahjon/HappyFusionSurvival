using System;
using System.Collections.Generic;
using Starter.Common.Input;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Starter.Common.Interactions
{
	/// <summary>
	/// Local-only scanner that finds the best <see cref="IInteractable"/> in front of
	/// the camera each frame and routes the Interact action to it. Targets self-describe
	/// their range, lock state, and what happens on interaction — so this stays mode-agnostic.
	///
	/// Two paths: tap Interact → <see cref="IInteractable.OnInteract"/>; hold Interact
	/// (for targets that also implement <see cref="IPickupableStation"/>) → pickup RPC.
	/// </summary>
	[RequireComponent(typeof(GameInputActions))]
	public sealed class InteractionScanner : MonoBehaviour
	{
		[Tooltip("Maximum radius scanned for interactables. Per-target InteractRange is still enforced; use this as an upper bound.")]
		public float ScanRadius = 5f;

		[Tooltip("Minimum dot(cameraForward, toTarget). 0 = anywhere in the front hemisphere; 0.5 ≈ within a 60° cone; 1 = exactly looking at it.")]
		[Range(-1f, 1f)]
		public float ViewConeDot = 0.85f;

		[Tooltip("Toast cooldown so locked-prompt messages don't spam.")]
		public float ToastCooldown = 1f;

		[Header("Feedback")]
		[Tooltip("Seconds the prompt's scale-punch pop lasts when an interaction fires.")]
		public float PunchDuration = 0.18f;
		[Tooltip("Extra scale at the peak of the punch (0.18 = pops to 1.18x then settles back).")]
		public float PunchScale = 0.18f;
		[Tooltip("Seconds the prompt takes to fade in/out instead of instantly popping on/off.")]
		public float FadeDuration = 0.1f;

		public event Action<string> Toast;

		/// <summary>Local-only singleton; set when the local player's scanner initializes.</summary>
		public static InteractionScanner LocalInstance { get; private set; }

		/// <summary>The component the local scanner has currently chosen as the interactable in focus (null when nothing in range/in cone). Read by InteractionPrompt to highlight itself.</summary>
		public static IInteractable CurrentInteractable { get; private set; }

		/// <summary>Transform of the currently chosen interactable (convenience accessor used by InteractionPrompt).
		/// Uses Unity-overloaded null-check so a target destroyed mid-frame (e.g. Runner.Despawn) returns null
		/// instead of throwing MissingReferenceException.</summary>
		public static Transform CurrentTarget
		{
			get
			{
				var c = CurrentInteractable as Component;
				return c != null ? c.transform : null;
			}
		}

		/// <summary>
		/// Transform whose pickup hold is currently in progress on the local client.
		/// InteractionPrompt reads this + <see cref="HoldProgress"/> to draw the radial fill.
		/// </summary>
		public static Transform HoldingTarget { get; private set; }

		/// <summary>0..1 fraction of the active hold timer. 0 when no hold is in progress.</summary>
		public static float HoldProgress { get; private set; }

		/// <summary>
		/// True when the local scanner is actively scanning this frame — i.e. cursor is locked AND
		/// no <see cref="IInteractionGate"/> is vetoing. False while a UI panel is open, the player
		/// is seated in a vehicle, etc. <see cref="InteractionPrompt"/> reads this to hide all
		/// world-space prompts during those states.
		/// </summary>
		public static bool IsScanningActive { get; private set; }

		/// <summary>
		/// Frame number when something already handled the Interact press this frame.
		/// Used to prevent multiple Interact subscribers (scanner + VehicleSession) from
		/// both acting on the same key press — first one to fire wins, the rest see this
		/// flag and skip.
		/// </summary>
		public static int InteractConsumedFrame = -1;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			LocalInstance = null;
			CurrentInteractable = null;
			HoldingTarget = null;
			HoldProgress = 0f;
			IsScanningActive = false;
			InteractConsumedFrame = -1;
		}

		private GameInputActions _actions;
		private bool _initialized;
		private float _toastSuppressUntil;
		private readonly List<IInteractionGate> _gates = new List<IInteractionGate>();

		// Single shared prompt HUD — built once in Initialize() for the local player only.
		private Camera _promptCamera;
		private RectTransform _canvasRT;
		private GameObject _promptGO;
		private RectTransform _promptRT;
		private Image _promptBox;
		private Image _promptHoldFill;
		private TextMeshProUGUI _promptCenterLabel;
		private CanvasGroup _promptCanvasGroup;

		// Scale-punch feedback: a quick pop the indicator graphic does when an interaction fires so
		// the press reads as tactile. Only the Box/HoldFill scale — the CenterLabel text stays put.
		// -1 = idle; otherwise the unscaled time the current punch began.
		private float _punchStartTime = -1f;
		private Vector3 _boxBaseScale = Vector3.one;
		private Vector3 _holdFillBaseScale = Vector3.one;

		// Cache for the prompt anchor's visual center. Renderer bounds are world-axis-aligned, so
		// we store the center as a target-local offset once per target and re-transform each frame —
		// keeps it correct as the object moves/rotates without re-walking renderers every frame.
		private Transform _visualCenterTarget;
		private Vector3 _visualCenterLocal;
		private readonly List<Renderer> _rendererScratch = new List<Renderer>();

		// Hold-to-pickup state. Tap path stays simple: fire OnInteract immediately on
		// release for non-pickupable targets. For pickupable targets we wait until
		// release to decide tap-vs-hold so the same key drives both.
		//
		// Hold-to-interact (revive etc.) shares the same press/timer fields but routes
		// through _pressHold instead of _pressPickup. Pickup takes priority if a target
		// implements both.
		private IInteractable _pressTarget;
		private IPickupableStation _pressPickup;
		private IHoldInteractable _pressHold;
		private float _pressStartTime;
		private bool _pickupFired;

		public void Initialize()
		{
			if (_initialized) return;

			_actions = GetComponent<GameInputActions>();

			if (_actions != null && _actions.IsInitialized)
			{
				_actions.Interact.performed += OnInteractPressed;
				_actions.Interact.canceled += OnInteractReleased;
			}

			LocalInstance = this;
			_initialized = true;
			BuildPromptHUD();
		}

		private void OnDestroy()
		{
			if (!_initialized) return;
			if (_actions != null && _actions.IsInitialized)
			{
				_actions.Interact.performed -= OnInteractPressed;
				_actions.Interact.canceled -= OnInteractReleased;
			}
			if (LocalInstance == this)
			{
				LocalInstance = null;
				CurrentInteractable = null;
				HoldingTarget = null;
				HoldProgress = 0f;
				IsScanningActive = false;
			}
			if (_promptGO != null) _promptGO.SetActive(false);
		}

		private void Update()
		{
			if (!_initialized) return;

			bool active = Cursor.lockState == CursorLockMode.Locked && InteractionsAllowed();
			IsScanningActive = active;

			if (!active)
			{
				CurrentInteractable = null;
				AbortHold();
				return;
			}

			CurrentInteractable = TryFindBest();

			TickHold();
		}

		private void TickHold()
		{
			if (_pressTarget == null) return;
			if (_pressPickup == null && _pressHold == null) return;
			if (_pickupFired) return;

			// Cancel hold if the player looked away from the target (or it disappeared).
			// Use a current-target probe that includes locked candidates so a hold-interactable
			// that briefly toggles CanInteract (e.g. revive completes and downed -> alive) still
			// resolves to the same component this frame and TickHold can fire one last time.
			if (CurrentInteractable != _pressTarget)
			{
				AbortHold();
				return;
			}

			float duration;
			if (_pressPickup != null)
			{
				// Cancel if the target became blocked mid-hold (e.g. someone else opened the crate).
				string blocked = _pressPickup.PickupBlockedReason;
				if (!string.IsNullOrEmpty(blocked))
				{
					AbortHold();
					return;
				}
				duration = Mathf.Max(0.01f, _pressPickup.PickupHoldSeconds);
			}
			else
			{
				// Hold-interactables cancel automatically if the target becomes uninteractable
				// mid-hold (e.g. another reviver finished first and the downed player is now alive).
				if (!_pressTarget.CanInteract)
				{
					AbortHold();
					return;
				}
				duration = Mathf.Max(0.01f, _pressHold.HoldSeconds);
			}

			float elapsed = Time.unscaledTime - _pressStartTime;
			HoldingTarget = (_pressTarget is Component c) ? c.transform : null;
			HoldProgress = Mathf.Clamp01(elapsed / duration);

			if (elapsed >= duration)
			{
				_pickupFired = true;
				InteractConsumedFrame = Time.frameCount;
				TriggerPromptPunch();

				if (_pressPickup != null)
				{
					_pressPickup.LocalRequestPickup();
				}
				else
				{
					_pressHold.LocalRequestHoldComplete();
				}

				// The pickup RPC despawns the target. For hold-interactables the target usually
				// flips CanInteract instead, but either way drop our references so consumers
				// (prompt UI, other scanner reads) can't dereference stale state this frame.
				CurrentInteractable = null;
				HoldingTarget = null;
				HoldProgress = 0f;
				// Clear the press refs so a later OnInteractReleased doesn't run a "tap" path on
				// the same press. Don't go through AbortHold() — that would send a HoldCancel
				// after the Complete fired, undoing the revive.
				_pressTarget = null;
				_pressPickup = null;
				_pressHold = null;
				_pressStartTime = 0f;
			}
		}

		private void OnInteractPressed(InputAction.CallbackContext ctx)
		{
			if (Cursor.lockState != CursorLockMode.Locked) return;
			if (!InteractionsAllowed()) return;
			if (InteractConsumedFrame == Time.frameCount) return;

			var best = TryFindBest(includeLocked: true);
			if (best == null) return;

			// Pickupable targets defer the tap decision to release; non-pickupable fire
			// the tap immediately on press (instant feedback, matches old behavior).
			var pickup = best as IPickupableStation;
			if (pickup != null && pickup.IsPickupable)
			{
				_pressTarget = best;
				_pressPickup = pickup;
				_pressHold = null;
				_pressStartTime = Time.unscaledTime;
				_pickupFired = false;
				HoldingTarget = (best is Component c) ? c.transform : null;
				HoldProgress = 0f;

				string blocked = pickup.PickupBlockedReason;
				if (!string.IsNullOrEmpty(blocked))
				{
					// Don't engage the hold path while blocked — toast and let the tap-on-release
					// path still open the UI (e.g. show "Empty first" but still let them open the crate).
					ShowToast(blocked);
				}
				return;
			}

			// Hold-only interactables: engage the hold path and notify the target (e.g. start
			// the "I'm reviving" RPC) — there's no tap path, so don't FireTap on release.
			var hold = best as IHoldInteractable;
			if (hold != null && best.CanInteract)
			{
				_pressTarget = best;
				_pressPickup = null;
				_pressHold = hold;
				_pressStartTime = Time.unscaledTime;
				_pickupFired = false;
				HoldingTarget = (best is Component c) ? c.transform : null;
				HoldProgress = 0f;
				InteractConsumedFrame = Time.frameCount;
				hold.LocalRequestHoldStart();
				return;
			}

			FireTap(best);
		}

		private void OnInteractReleased(InputAction.CallbackContext ctx)
		{
			// On release, if we engaged the hold path but never finished it, treat as a tap.
			// Skipped for hold-only interactables (revive etc.) — they have no tap action,
			// so an early release should just cancel the hold via AbortHold below.
			if (_pressTarget != null && !_pickupFired && _pressHold == null)
			{
				// Only fire if the player is still looking at the same target and it's allowed.
				if (CurrentInteractable == _pressTarget && InteractConsumedFrame != Time.frameCount)
				{
					FireTap(_pressTarget);
				}
			}
			AbortHold();
		}

		/// <summary>Kick off the shared prompt's scale-punch so a successful Interact reads as a tactile pop.</summary>
		private void TriggerPromptPunch()
		{
			_punchStartTime = Time.unscaledTime;
		}

		private void FireTap(IInteractable target)
		{
			if (target.CanInteract)
			{
				InteractConsumedFrame = Time.frameCount;
				TriggerPromptPunch();
				target.OnInteract(this);
			}
			else if (!string.IsNullOrEmpty(target.LockedReason))
			{
				InteractConsumedFrame = Time.frameCount;
				ShowToast(target.LockedReason);
			}
		}

		private void AbortHold()
		{
			// Notify hold-interactables that the hold was abandoned so they can drop reviver
			// state (++/-- counters etc.). Pickups don't need a cancel notification — they
			// only act on completion, and we never sent a "start" RPC to undo.
			if (_pressHold != null && !_pickupFired)
			{
				_pressHold.LocalRequestHoldCancel();
			}

			_pressTarget = null;
			_pressPickup = null;
			_pressHold = null;
			_pressStartTime = 0f;
			_pickupFired = false;
			HoldingTarget = null;
			HoldProgress = 0f;
		}

		/// <summary>
		/// Polled before each scan/interact. Returns false if ANY <see cref="IInteractionGate"/>
		/// sibling on the player root vetoes (e.g. an inventory UI is open, or the player is
		/// already seated). All gates are ANDed together — important now that the player has
		/// multiple gate sources (LootSession, CraftingSession, ComputerSession, VehicleSession).
		/// </summary>
		private bool InteractionsAllowed()
		{
			_gates.Clear();
			GetComponents(_gates);
			for (int i = 0; i < _gates.Count; i++)
			{
				if (!_gates[i].AllowInteractions) return false;
			}
			return true;
		}

		// Reused scratch buffers so the per-frame scan stays alloc-free.
		private readonly List<IInteractable> _scanCandidates = new List<IInteractable>();
		private readonly Dictionary<object, IInteractable> _groupReps = new Dictionary<object, IInteractable>();

		private IInteractable TryFindBest(bool includeLocked = false)
		{
			var camT = Camera.main != null ? Camera.main.transform : transform;
			Vector3 camPos = camT.position;
			Vector3 camFwd = camT.forward;
			Vector3 playerPos = transform.position;

			_scanCandidates.Clear();
			_groupReps.Clear();

			Transform selfRoot = transform;
			var hits = Physics.OverlapSphere(playerPos, ScanRadius);
			for (int i = 0; i < hits.Length; i++)
			{
				var col = hits[i];
				if (col == null) continue;

				var candidate = col.GetComponentInParent<IInteractable>();
				if (candidate == null) continue;
				// Skip self — the local player's own KCC capsule sits in OverlapSphere range and
				// the player root carries the (downed-state) IInteractable. Without this filter
				// a downed player could trigger their own revive prompt.
				if (candidate is Component cmp && cmp.transform == selfRoot) continue;
				if (!candidate.CanInteract)
				{
					// Not actionable right now. Keep it as a candidate only when we're scanning locked targets
					// AND it has something to surface (a LockedReason toast). A dormant IInteractable with nothing
					// to act on and nothing to say — e.g. a healthy player/bot whose downed-revive interface is
					// inactive — is pure noise; dropping it stops it from shadowing a real target (like a Door)
					// behind it when it happens to be more centered in view.
					if (!includeLocked || string.IsNullOrEmpty(candidate.LockedReason)) continue;
				}

				Vector3 point = candidate.InteractionPoint;
				float range = candidate.InteractRange;
				if ((point - playerPos).sqrMagnitude > range * range) continue;

				// OverlapSphere can return several colliders that resolve to the same interactable
				// (multi-collider rigs); de-dupe so it isn't scored twice and group reps are sane.
				if (_scanCandidates.Contains(candidate)) continue;

				// Collapse clustered interactables (e.g. a vehicle's seats) to the closest member,
				// so you can't reach the far seat through the cab from the near door. An interactable
				// member is preferred over a locked one at equal-or-greater distance.
				if (candidate is IInteractableGroup grouped && grouped.InteractionGroupKey != null)
				{
					object key = grouped.InteractionGroupKey;
					if (_groupReps.TryGetValue(key, out var existing))
					{
						if (PreferGroupMember(existing, candidate, playerPos)) continue;
						_scanCandidates.Remove(existing);
					}
					_groupReps[key] = candidate;
				}

				_scanCandidates.Add(candidate);
			}

			IInteractable best = null;
			float bestScore = ViewConeDot;
			for (int i = 0; i < _scanCandidates.Count; i++)
			{
				var candidate = _scanCandidates[i];
				float score = LookAlignment(candidate.InteractionPoint, camPos, camFwd);
				if (score <= bestScore) continue;
				bestScore = score;
				best = candidate;
			}

			return best;
		}

		/// <summary>
		/// Returns true if <paramref name="existing"/> should remain the group's representative
		/// over <paramref name="contender"/>. Interactable members win over locked ones; among
		/// members of equal lock state, the nearer to the player wins.
		/// </summary>
		private static bool PreferGroupMember(IInteractable existing, IInteractable contender, Vector3 playerPos)
		{
			if (existing.CanInteract != contender.CanInteract)
				return existing.CanInteract;

			float dExisting = (existing.InteractionPoint - playerPos).sqrMagnitude;
			float dContender = (contender.InteractionPoint - playerPos).sqrMagnitude;
			return dExisting <= dContender;
		}

		private static float LookAlignment(Vector3 targetPos, Vector3 camPos, Vector3 camFwd)
		{
			var to = targetPos - camPos;
			float lenSq = to.sqrMagnitude;
			if (lenSq < 0.0001f) return 1f;
			return Vector3.Dot(camFwd, to) / Mathf.Sqrt(lenSq);
		}

		private static RectTransform FindMainCanvasRT()
		{
			// Find the root Screen Space Overlay canvas — ignores any in-world or sub-canvases.
			foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
			{
				if (c.renderMode == RenderMode.ScreenSpaceOverlay && c.transform.parent == null)
					return (RectTransform)c.transform;
			}
			return null;
		}

		private void BuildPromptHUD()
		{
			var visuals = FindAnyObjectByType<InteractionPromptVisuals>(FindObjectsInactive.Include);
			_canvasRT = FindMainCanvasRT();

			// Fallback: if the scene doesn't already place an InteractionPromptVisuals,
			// instantiate the shipped Resources prefab under the main canvas so prompts
			// still render. Lets new scenes work without manual canvas setup.
			if (visuals == null && _canvasRT != null)
			{
				var prefab = Resources.Load<GameObject>("InteractionPromptHUD");
				if (prefab != null)
				{
					var instance = Instantiate(prefab);
					instance.name = prefab.name;
					instance.transform.SetParent(_canvasRT, false);
					visuals = instance.GetComponent<InteractionPromptVisuals>();
				}
			}

			if (visuals == null) { Debug.LogWarning("[InteractionScanner] No InteractionPromptVisuals available — prompt will not render."); return; }

			_promptGO = visuals.gameObject;
			_promptRT = (RectTransform)visuals.transform;
			_promptBox = visuals.Box;
			_promptHoldFill = visuals.HoldFill;
			_promptCenterLabel = visuals.CenterLabel;

			if (_promptBox != null) _boxBaseScale = _promptBox.rectTransform.localScale;
			if (_promptHoldFill != null) _holdFillBaseScale = _promptHoldFill.rectTransform.localScale;

			// Drive show/hide as an alpha fade rather than SetActive pops. CanvasGroup covers the
			// whole prompt (indicator + label) in one lerp; added at runtime so the prefab needn't carry it.
			_promptCanvasGroup = _promptGO.GetComponent<CanvasGroup>();
			if (_promptCanvasGroup == null) _promptCanvasGroup = _promptGO.AddComponent<CanvasGroup>();
			_promptCanvasGroup.alpha = 0f;

			if (_promptHoldFill != null)
			{
				_promptHoldFill.fillAmount = 0f;
				_promptHoldFill.enabled = false;
			}
			if (_promptCenterLabel != null)
				_promptCenterLabel.enabled = false;

			_promptGO.SetActive(false);
		}

		private void LateUpdate()
		{
			if (!_initialized || _promptGO == null) return;

			if (_promptCamera == null && Camera.main != null)
				_promptCamera = Camera.main;
			if (_promptCamera == null) return;

			// Refresh content (position/colour/label) only when there's something to point at;
			// otherwise leave the last frame's content in place and let it fade out.
			bool wantVisible = UpdatePromptContent();

			// Fade the whole prompt toward the target opacity instead of popping on/off. Unscaled
			// time so it still animates while paused. Keep the GO active until fully faded out.
			float targetAlpha = wantVisible ? 1f : 0f;
			if (_promptCanvasGroup != null)
			{
				if (!Mathf.Approximately(_promptCanvasGroup.alpha, targetAlpha))
				{
					float step = Time.unscaledDeltaTime / Mathf.Max(0.0001f, FadeDuration);
					_promptCanvasGroup.alpha = Mathf.MoveTowards(_promptCanvasGroup.alpha, targetAlpha, step);
				}
			}

			bool shouldBeActive = wantVisible || (_promptCanvasGroup != null && _promptCanvasGroup.alpha > 0.001f);
			if (_promptGO.activeSelf != shouldBeActive) _promptGO.SetActive(shouldBeActive);
		}

		/// <summary>
		/// Positions the shared prompt over the current target and refreshes its colours, hold
		/// fill, label, and scale-punch. Returns true if the prompt should be shown this frame, or
		/// false (without touching content) when there's nothing to point at — so the caller can
		/// fade it out in place.
		/// </summary>
		private bool UpdatePromptContent()
		{
			Transform target = CurrentTarget;
			if (target == null || !IsScanningActive)
				return false;

			var prompt = target.GetComponent<InteractionPrompt>();

			if (prompt != null && prompt.HideWhenLocked && CurrentInteractable != null && !CurrentInteractable.CanInteract)
				return false;

			// Anchor base: an explicit override transform wins; otherwise the asset's visual
			// center (renderer bounds) so the prompt sits on the middle of the mesh instead of
			// the transform pivot, which is usually at the model's base → prompt at the bottom.
			var anchorOverride = target.GetComponent<IInteractionPromptAnchor>()?.PromptAnchor;
			Transform anchorT = anchorOverride != null ? anchorOverride : target;
			Vector3 anchorBase = anchorOverride != null ? anchorT.position : GetVisualCenter(target);
			Vector3 anchor = anchorBase + (prompt != null ? anchorT.TransformVector(prompt.LocalOffset) : Vector3.zero);
			Vector3 screenPos = _promptCamera.WorldToScreenPoint(anchor);
			if (screenPos.z < 0f)
				return false;

			if (_canvasRT != null)
			{
				RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRT, screenPos, null, out Vector2 localPos);
				_promptRT.anchoredPosition = localPos;
			}

			// Transient scale-punch on the indicator graphic only (Box/HoldFill, not the text label):
			// a half-sine pop (1 -> 1+PunchScale -> 1) over PunchDuration confirming the Interact
			// registered. Uses unscaled time so it reads even if the game pauses.
			float punchMul = 1f;
			if (_punchStartTime >= 0f)
			{
				float t = (Time.unscaledTime - _punchStartTime) / Mathf.Max(0.01f, PunchDuration);
				if (t >= 1f) _punchStartTime = -1f;
				else punchMul = 1f + PunchScale * Mathf.Sin(t * Mathf.PI);
			}
			if (_promptBox != null) _promptBox.rectTransform.localScale = _boxBaseScale * punchMul;
			if (_promptHoldFill != null) _promptHoldFill.rectTransform.localScale = _holdFillBaseScale * punchMul;

			_promptBox.color = prompt != null ? prompt.ActiveColor : new Color(0.3f, 0.65f, 1f, 1f);

			Color holdColor = prompt != null ? prompt.HoldColor : new Color(1f, 0.85f, 0.2f, 1f);
			bool holding = HoldingTarget == target;
			float fill = holding ? HoldProgress : 0f;
			_promptHoldFill.color = holdColor;
			if (_promptHoldFill.fillAmount != fill) _promptHoldFill.fillAmount = fill;
			if (_promptHoldFill.enabled != holding) _promptHoldFill.enabled = holding;

			string centerText = (prompt != null && prompt.SuppressLabel)
				? string.Empty
				: (!string.IsNullOrEmpty(prompt?.CenterLabelText)
					? prompt.CenterLabelText
					: CurrentInteractable?.InteractLabel ?? string.Empty);
			bool hasCenterLabel = !string.IsNullOrEmpty(centerText);
			if (_promptCenterLabel != null)
			{
				if (_promptCenterLabel.enabled != hasCenterLabel) _promptCenterLabel.enabled = hasCenterLabel;
				if (hasCenterLabel) _promptCenterLabel.text = centerText;
			}

			return true;
		}

		/// <summary>
		/// World-space center of the target's combined renderer bounds — used as the default
		/// prompt anchor so the indicator sits on the middle of the mesh rather than the
		/// transform pivot (typically at the model's base). Cached per target as a local-space
		/// offset and re-transformed each frame; falls back to the pivot if no renderers exist.
		/// </summary>
		private Vector3 GetVisualCenter(Transform target)
		{
			if (target != _visualCenterTarget)
			{
				_visualCenterTarget = target;
				_visualCenterLocal = Vector3.zero;

				target.GetComponentsInChildren(false, _rendererScratch);
				bool any = false;
				Bounds bounds = default;
				for (int i = 0; i < _rendererScratch.Count; i++)
				{
					var r = _rendererScratch[i];
					// Skip UI/particle/other non-mesh renderers that would skew the center.
					if (r is MeshRenderer || r is SkinnedMeshRenderer)
					{
						if (!any) { bounds = r.bounds; any = true; }
						else bounds.Encapsulate(r.bounds);
					}
				}
				_rendererScratch.Clear();

				if (any)
					_visualCenterLocal = target.InverseTransformPoint(bounds.center);
			}

			return target.TransformPoint(_visualCenterLocal);
		}

		private void ShowToast(string text)
		{
			if (Time.unscaledTime < _toastSuppressUntil) return;
			_toastSuppressUntil = Time.unscaledTime + ToastCooldown;
			Debug.Log($"[InteractionScanner] {text}");
			Toast?.Invoke(text);
		}
	}

	/// <summary>
	/// Optional sibling on the player root that gates whether the scanner is allowed
	/// to operate this frame. Used by <c>LootSession</c> to suppress interactions
	/// while a loot UI is open without coupling the scanner to inventory code.
	/// </summary>
	public interface IInteractionGate
	{
		bool AllowInteractions { get; }
	}
}
