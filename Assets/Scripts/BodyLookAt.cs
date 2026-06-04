using Starter.Shooter;
using UnityEngine;

/// <summary>
/// Rotates the humanoid head bone to match the player's camera pitch (up/down look).
/// Attach to the same GameObject as the body Animator (the "Player" child under VisualRoot).
///
/// Uses Animator.GetBoneTransform(HumanBodyBones.Head) so it works for any humanoid mesh —
/// never hardcodes a bone path.  The pitch angle is read directly from the parent Player's KCC.
/// Applied in LateUpdate after the Animator has written its pose.
/// </summary>
[RequireComponent(typeof(Animator))]
public class BodyLookAt : MonoBehaviour
{
    [Tooltip("How strongly the head tracks the camera pitch (0 = animation only, 1 = fully tracks camera).")]
    [Range(0f, 1f)] public float Weight = 1f;

    [Tooltip("Maximum degrees the head can tilt up (positive value).")]
    public float MaxLookUp   = 50f;
    [Tooltip("Maximum degrees the head can tilt down (positive value).")]
    public float MaxLookDown = 40f;

    private Animator  _animator;
    private Transform _headBone;
    private Player    _player;
    private Quaternion _headBindLocalRot;

    private void Awake()
    {
        _animator = GetComponent<Animator>();

        // Resolve head bone from the humanoid avatar — works for any mesh.
        if (_animator != null && _animator.isHuman)
        {
            _headBone = _animator.GetBoneTransform(HumanBodyBones.Head);
            if (_headBone != null)
                _headBindLocalRot = _headBone.localRotation;
        }

        // Find the owning Player up the hierarchy — no Inspector wiring needed.
        _player = GetComponentInParent<Player>();
    }

    private void LateUpdate()
    {
        if (_headBone == null || _player == null || Weight <= 0f) return;

        // Camera pitch in degrees from the KCC. Negative = looking up, positive = looking down.
        float pitch = _player.KCC.GetLookRotation(true, false).x;
        pitch = Mathf.Clamp(pitch, -MaxLookUp, MaxLookDown);

        // Apply the pitch on top of the bind-pose local rotation so it works
        // regardless of how the animation moves the rest of the spine.
        Quaternion target = _headBindLocalRot * Quaternion.Euler(pitch, 0f, 0f);
        _headBone.localRotation = Quaternion.Slerp(_headBone.localRotation, target, Weight);
    }
}
