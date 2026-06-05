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
public class Door : NetworkBehaviour, IInteractable, IDoorBarrier
{
    [Header("Door Settings")]
    public Transform doorPivotTransform;
    public float     pivotAngle  = 90f;
    public float     openSpeed   = 2f;
    public bool      openInwards = false;

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

    // ── Local rotation cache ───────────────────────────────────────────────────
    Quaternion _closedRotation;
    Quaternion _openRotation;

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    public override void Spawned()
    {
        if (doorPivotTransform == null)
            doorPivotTransform = transform;

        _closedRotation = doorPivotTransform.localRotation;
        float angle     = openInwards ? -pivotAngle : pivotAngle;
        _openRotation   = _closedRotation * Quaternion.Euler(0f, angle, 0f);
    }

    /// <summary>Smooth lerp toward the target rotation every render frame (all clients).</summary>
    public override void Render()
    {
        if (doorPivotTransform == null) return;

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
        // Re-validate that the sender is still in range
        var playerObj = Runner.GetPlayerObject(info.Source);
        if (playerObj != null)
        {
            float dist = Vector3.Distance(playerObj.transform.position, InteractionPoint);
            if (dist > InteractRange * 1.25f) return;
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
