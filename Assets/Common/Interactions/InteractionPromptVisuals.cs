using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Starter.Common.Interactions
{
    /// <summary>
    /// Wires up the visual elements inside the InteractionPrompt prefab.
    /// Assign once on the prefab; InteractionScanner reads these at runtime.
    /// </summary>
    public sealed class InteractionPromptVisuals : MonoBehaviour
    {
        public Image Box;
        public Image HoldFill;
        public TextMeshProUGUI CenterLabel;
    }
}
