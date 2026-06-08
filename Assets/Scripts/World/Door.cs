using Fusion;
using Sirenix.OdinInspector;
using Starter.Common.Interactions;
using Starter.Shooter;
using UnityEngine;

/// <summary>
/// Networked interactable door. Any player in range can toggle it open/closed.
/// Visual rotation is smoothly lerped on every client in Render().
/// Audio plays locally via AudioManager when the networked state flips.
/// </summary>
[RequireComponent(typeof(InteractionPrompt))]
public class Door : NetworkBehaviour, IInteractable, IDoorBarrier
{
    [Header("Door Settings")]
    public Transform doorPivotTransform;
    public float     pivotAngle  = 90f;
    public float     openSpeed   = 2f;
    public bool      openInwards = false;
    [Tooltip("When enabled, the door always swings away from the player who opened it.")]
    public bool      dynamicOpenDirection = false;

    public string openInteractLabel  = "Open door";
    public string closeInteractLabel = "Close door";

    [Header("Audio")]
    public AudioClip doorOpenSound;
    public AudioClip doorCloseSound;

    // ── IInteractable ──────────────────────────────────────────────────────────
    public float   InteractRange     => 2.5f;
    public bool    CanInteract       => true;
    public Vector3 InteractionPoint  => transform.position;
    public string  LockedReason      => null;
    public string  InteractLabel     => IsOpen ? closeInteractLabel : openInteractLabel;

    // ── Networked state ────────────────────────────────────────────────────────
    [Networked, OnChangedRender(nameof(OnIsOpenChanged))]
    public bool IsOpen { get; set; }

    // ── Networked & local state ────────────────────────────────────────────────
    /// <summary>Networked open angle (degrees). Set by state authority when dynamicOpenDirection is on.</summary>
    [Networked] private float _networkedOpenAngle { get; set; }

    Quaternion _closedRotation;
    Quaternion _openRotation;

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    public override void Spawned()
    {
        if (doorPivotTransform == null)
            doorPivotTransform = transform;

        _closedRotation = doorPivotTransform.localRotation;
        RefreshOpenRotation();
    }

    /// <summary>Recomputes _openRotation from the current angle (static or networked).</summary>
    void RefreshOpenRotation()
    {
        float angle   = dynamicOpenDirection ? _networkedOpenAngle : (openInwards ? -pivotAngle : pivotAngle);
        _openRotation = _closedRotation * Quaternion.Euler(0f, angle, 0f);
    }

    /// <summary>Smooth lerp toward the target rotation every render frame (all clients).</summary>
    public override void Render()
    {
        if (doorPivotTransform == null) return;

        if (dynamicOpenDirection) RefreshOpenRotation();

        Quaternion target = IsOpen ? _openRotation : _closedRotation;
        doorPivotTransform.localRotation = Quaternion.Lerp(
            doorPivotTransform.localRotation,
            target,
            Time.deltaTime * openSpeed * 10f);
    }

    // ── IInteractable ──────────────────────────────────────────────────────────
    public void OnInteract(InteractionScanner scanner)
    {
        RpcToggleDoor();
    }

    // ── IDoorBarrier (AI path-clearing) ─────────────────────────────────────────
    // Bots run on the state authority and have no connection / NetworkRunner.GetPlayerObject entry, so they
    // can't drive the player-validated RpcToggleDoor path — they flip the networked state directly.
    public bool IsPassable => IsOpen;
    public void AuthorityForceOpen()
    {
        if (HasStateAuthority && IsOpen == false)
            IsOpen = true;
    }

    // ── RPC ────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Any client can call this; executes only on state authority.
    /// Range is re-validated server-side before the toggle is accepted.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RpcToggleDoor(RpcInfo info = default)
    {
        var source    = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
        var playerObj = Runner.GetPlayerObject(source);

        // Re-validate that the sender is still in range
        if (playerObj != null)
        {
            float dist = Vector3.Distance(playerObj.transform.position, InteractionPoint);
            if (dist > InteractRange * 1.25f) return;
        }

        // When opening with dynamic direction, determine which side of the door the player is on
        // and set the swing angle so the door always opens away from them.
        if (dynamicOpenDirection && !IsOpen && playerObj != null)
        {
            Vector3 localPlayerPos = transform.InverseTransformPoint(playerObj.transform.position);
            _networkedOpenAngle = localPlayerPos.z >= 0f ? -pivotAngle : pivotAngle;
        }

        IsOpen = !IsOpen;
    }

    // ── OnChanged render callback ──────────────────────────────────────────────
    void OnIsOpenChanged()
    {
        AudioClip clip = IsOpen ? doorOpenSound : doorCloseSound;
        if (clip != null)
            AudioManager.Instance?.PlaySFX(clip, transform.position);
    }
}
