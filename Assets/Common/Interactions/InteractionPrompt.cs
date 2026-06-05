using UnityEngine;

namespace Starter.Common.Interactions
{
    /// <summary>
    /// Per-interactable data read by <see cref="InteractionScanner"/> to customise the shared
    /// prompt HUD. Add this to any interactable to override offset, label text, or colours.
    /// No GameObjects are created here — the scanner owns the single prompt instance.
    /// </summary>
    public sealed class InteractionPrompt : MonoBehaviour
    {
        [Header("Layout")]
        public Vector3 LocalOffset = Vector3.zero;

        [Header("Label")]
        public string CenterLabelText = string.Empty;
        [Tooltip("Suppress the InteractLabel from code — use when this component is only here for visuals/art.")]
        public bool SuppressLabel = false;

        [Header("Visibility")]
        [Tooltip("Hide the prompt when CanInteract is false (e.g. already-opened chest).")]
        public bool HideWhenLocked = false;

        [Header("Colors")]
        public Color ActiveColor = new Color(0.3f, 0.65f, 1f, 1f);
        public Color InactiveColor = new Color(0.3f, 0.65f, 1f, 0.35f);
        public Color HoldColor = new Color(1f, 0.85f, 0.2f, 1f);

        [Header("Audio")]
        [Tooltip("Optional one-shot played locally when this interaction fires (tap, or hold/pickup completion). Leave empty for no sound.")]
        public AudioClip InteractSound;
        [Range(0f, 1f)] public float InteractVolume = 1f;
    }
}
