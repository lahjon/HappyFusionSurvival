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

    /// <summary>Animator trigger name — matches the clip name.</summary>
    public string TriggerName => Clip != null ? Clip.name : string.Empty;

    /// <summary>Full duration of the clip in seconds.</summary>
    public float Duration => Clip != null ? Clip.length : 0f;
}
