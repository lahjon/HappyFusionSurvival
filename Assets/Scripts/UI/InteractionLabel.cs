using Starter.Common.Interactions;
using TMPro;
using UnityEngine;

namespace Starter.Shooter
{
    public sealed class InteractionLabel : MonoBehaviour
    {
        public TextMeshProUGUI Label;

        private void Update()
        {
            var target = InteractionScanner.CurrentInteractable;
            if (target == null || !InteractionScanner.IsScanningActive)
            {
                Label.enabled = false;
                return;
            }

            Label.enabled = true;
            Label.text = target.CanInteract ? target.InteractLabel : target.LockedReason;
        }
    }
}
