using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Starter.Common.Interactions
{
	/// <summary>
	/// Optional sibling that overrides consume/cooldown state for <see cref="BaseInteractable"/>.
	/// Implement this on a <c>NetworkBehaviour</c> to replicate state across peers, or on a
	/// plain <c>MonoBehaviour</c> for any other custom logic. If absent, <see cref="BaseInteractable"/>
	/// tracks state locally.
	/// </summary>
	public interface IInteractableState
	{
		/// <summary>True when the object has been consumed and can no longer be interacted with.</summary>
		bool IsConsumed { get; }

		/// <summary>True when the cooldown is still running and the object cannot be used yet.</summary>
		bool IsOnCooldown { get; }

		/// <summary>Called by BaseInteractable after a consumable interaction fires.</summary>
		void OnConsumed();

		/// <summary>Called by BaseInteractable after a non-consumable interaction fires. <paramref name="cooldown"/> is the configured cooldown in seconds (0 = none).</summary>
		void OnUsed(float cooldown);
	}

	/// <summary>
	/// Generic local-only <see cref="IInteractable"/> for scene props that just need to
	/// fire a UnityEvent when the player interacts. Drop this on any GameObject (or use
	/// the <c>BaseInteractable</c> prefab) and wire <see cref="OnInteracted"/> in the
	/// Inspector to drive any local response — open a UI, play VFX, invoke an RPC on a
	/// sibling NetworkBehaviour, etc.
	///
	/// For interactions that mutate networked state, wire the event to a method that
	/// calls an [Rpc] on a NetworkBehaviour sibling — this component itself stays local
	/// so it can be used on pure-visual props without forcing a NetworkObject.
	/// Add a sibling <see cref="IInteractableState"/> to override consume/cooldown with networked state.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(InteractionPrompt))]
	public sealed class BaseInteractable : MonoBehaviour, IInteractable, IInteractionPromptAnchor
	{
		[Header("Authoring")]
		[Tooltip("Max distance from the local player at which this can be interacted with.")]
		[Min(0f)] public float InteractRangeValue = 2f;

		[Tooltip("Text shown in the interaction HUD when this is the current target (e.g. \"Open door\", \"Pull lever\").")]
		public string InteractLabelText = "Press E to interact";

		[Tooltip("Optional toast shown when CanInteract is false (e.g. \"Locked\", \"In use\"). Leave empty for silent.")]
		public string LockedReasonText = string.Empty;

		[Tooltip("Optional offset (local space) for the point the scanner uses for range + line-of-sight checks. Leave zero to use transform.position.")]
		public Vector3 InteractionPointOffset = Vector3.zero;

		[Tooltip("Optional transform the on-screen prompt indicator should follow. Leave unset to anchor the indicator to this object's transform.")]
		public Transform OverrideTransform;

		[Header("State")]
		[Tooltip("Toggle at runtime to enable/disable this interactable without removing the component.")]
		public bool Interactable = true;

		[Header("Consumable")]
		[Tooltip("If true, this can only be interacted with once.")]
		public bool IsConsumable = false;
		[Tooltip("Destroy the GameObject after it is consumed. Only used when IsConsumable is true.")]
		public bool DestroyOnConsume = false;
		
		[Tooltip("Seconds before this can be used again. 0 = no cooldown. Ignored when IsConsumable is true.")]
		[Min(0f)] public float Cooldown = 0f;

		[Header("SFX")]
		[Tooltip("Clip played locally when interacted with. Leave empty for no sound.")]
		public AudioClip SfxClip;
		[Range(0f, 1f)] public float SfxVolume = 1f;
		[Tooltip("Seconds after interact before the clip plays. 0 = immediate.")]
		[Min(0f)] public float SfxDelay = 0f;

		[Header("Events")]
		[Tooltip("Fires locally on whoever interacted. For multiplayer state changes, wire this to a method that invokes an RPC.")]
		public UnityEvent OnInteracted;

		/// <summary>The scanner that triggered the most recent OnInteracted (null before any interaction). Lets event handlers locate the local player without needing a parameter.</summary>
		public InteractionScanner LastScanner { get; private set; }

		// Local fallback state — used when no IInteractableState sibling is present.
		private bool _consumed;
		private float _cooldownUntil;
		private IInteractableState _state;

		private void Awake() => _state = GetComponent<IInteractableState>();

		private IEnumerator PlaySfxDelayed()
		{
			yield return new WaitForSeconds(SfxDelay);
			Starter.Shooter.AudioManager.Instance?.PlaySFX2D(SfxClip, SfxVolume);
		}

		// --- IInteractable ---

		float IInteractable.InteractRange => InteractRangeValue;
		bool IInteractable.CanInteract
		{
			get
			{
				if (!Interactable || !isActiveAndEnabled) return false;
				if (IsConsumable) return _state != null ? !_state.IsConsumed : !_consumed;
				if (Cooldown > 0f) return _state != null ? !_state.IsOnCooldown : Time.unscaledTime >= _cooldownUntil;
				return true;
			}
		}
		Vector3 IInteractable.InteractionPoint => transform.TransformPoint(InteractionPointOffset);
		Transform IInteractionPromptAnchor.PromptAnchor => OverrideTransform;
		string IInteractable.LockedReason => LockedReasonText;
		string IInteractable.InteractLabel => InteractLabelText;

		void IInteractable.OnInteract(InteractionScanner scanner)
		{
			LastScanner = scanner;
			if (SfxClip != null)
			{
				if (SfxDelay > 0f)
					StartCoroutine(PlaySfxDelayed());
				else
					Starter.Shooter.AudioManager.Instance?.PlaySFX2D(SfxClip, SfxVolume);
			}
			OnInteracted?.Invoke();

			if (IsConsumable)
			{
				if (_state != null) _state.OnConsumed();
				else _consumed = true;
				if (DestroyOnConsume) Destroy(gameObject);
			}
			else
			{
				if (_state != null) _state.OnUsed(Cooldown);
				else if (Cooldown > 0f) _cooldownUntil = Time.unscaledTime + Cooldown;
			}
		}
	}
}
