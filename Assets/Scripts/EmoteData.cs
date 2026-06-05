using UnityEngine;

/// <summary>
/// Data asset defining a single emote. Create via Assets → Create → HappyFusion → Emote Definition.
/// The trigger name and duration are derived directly from the clip — no manual entry needed.
/// </summary>
[CreateAssetMenu(menuName = "HappyFusion/Emote Data", fileName = "Emote_New")]
public class EmoteData : ScriptableObject
{
    [Tooltip("The animation clip to play. The clip name must match the trigger in the ThirdPerson animator controller.")]
    public AnimationClip Clip;

    [Tooltip("Name shown on the emote wheel.")]
    public string DisplayName;

    [Tooltip("Icon shown on the emote wheel.")]
    public Sprite Icon;

    [Header("Audio")]
    [Tooltip("Optional sound to play when the emote starts.")]
    public AudioClip EmoteAudio;
    [Tooltip("Delay in seconds before the audio plays after the emote starts.")]
    public float AudioDelay = 0f;
    [Tooltip("Playback volume (0–1).")]
    [Range(0f, 1f)] public float AudioVolume = 1f;

    [Header("Camera")]
    [Tooltip("When true, the camera pulls back to show the full body during the emote.")]
    public bool ShowFullBody = false;
    [Tooltip("Local-space offset applied to the camera while ShowFullBody is active (e.g. 0, 1, -4 pulls back and up).")]
    public Vector3 ThirdPersonOffset = new Vector3(0f, 0.25f, -3f);
    [Tooltip("How fast the camera transitions in and out of the third-person offset (units/sec).")]
    public float CameraTransitionSpeed = 10f;

    [Header("Playback")]
    [Tooltip("Playback speed of the animation. 1 = normal, 2 = double speed, 0.5 = half speed.")]
    [Min(0.01f)] public float AnimationSpeed = 1f;

    /// <summary>Animator trigger name — matches the clip name.</summary>
    public string TriggerName => Clip != null ? Clip.name : string.Empty;

    /// <summary>Actual playback duration accounting for AnimationSpeed.</summary>
    public float Duration => Clip != null ? Clip.length / AnimationSpeed : 0f;
}
